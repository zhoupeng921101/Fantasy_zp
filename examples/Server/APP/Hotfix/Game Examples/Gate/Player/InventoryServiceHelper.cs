using System.Collections.Generic;
using System.Globalization;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using GameConfig;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 背包系统服务端权威裁决核心(道具是玩家资产,服务端 players 文档唯一权威,客户端只持投影)。
///
/// 两轨:
///   - 堆叠轨 PlayerDoc.ItemHoldings(itemId → 数量):无独立状态的可堆叠道具,同种合并为一个总数(既有,由 ItemHoldingsServiceHelper 发放)。
///   - 批次轨 PlayerDoc.ItemLots(每次获得一条批次,带过期时刻):有有效期道具,分批领取分批过期,按 ExpireMs FIFO 扣减。
/// 道具是限时(TbItemDef.Term != 0)走批次轨,否则走堆叠轨。
///
/// 三条能力:
///   1. GrantLot —— 服务端权威发放限时道具(算绝对过期时刻 → 追加批次)。堆叠道具仍走 ItemHoldingsServiceHelper.GrantItem。
///   2. UseItem —— 使用道具专用事务:校验 → 扣道具(批次 FIFO / 堆叠 $inc) + 产出货币折叠进同一原子命令 → 记流水 → 回执 + 推送;
///      reqSeq 请求级幂等(防弱网重发双扣);批次数组读改写走 InventoryVersion 乐观并发。
///   3. SettleExpiredLotsAtLogin —— 惰性过期:登录时剔除已过期批次(无补偿的删库 + 记流水;有补偿的留库待后续结算)。
///
/// 有效期:过期时刻在获得时由服务端时钟固化为绝对 Unix 毫秒,之后只做比较;过期判定用服务端权威时钟,不信客户端本地时钟。
/// 使用产出:本轮只支持货币效果(TbItemDef.UseEffect==1 num),num_id 经 TbCurrency 映射到服务端 PropertyType(Piety/Diamond/Energy)。
/// </summary>
public static class InventoryServiceHelper
{
    /// <summary>使用数量 sanity 上限(防 perItem*count 溢出 + 荒谬批量;正常使用远不可能撞顶)。</summary>
    private const long MaxUseCount = 1_000_000L;

    /// <summary>批次数组读改写乐观并发重试上限(版本冲突时重读重算)。</summary>
    private const int MaxCasRetry = 3;

    // ==================== 使用事务 ====================

    /// <summary>使用道具裁决结果(handler 据此回响应 + 起推送)。</summary>
    public readonly struct UseResult
    {
        public readonly UseItemResultCode Code;
        public readonly long ConsumedCount;
        public readonly bool HasProduce;
        public readonly PropertyType ProducedType;
        public readonly long ProducedAmountTotal;
        public readonly long ProducedNewBalance;
        /// <summary>成功时的变更后文档(供构建背包推送);失败为 null。</summary>
        public readonly PlayerDoc? DocAfter;

        public UseResult(UseItemResultCode code, long consumedCount = 0, bool hasProduce = false,
            PropertyType producedType = default, long producedAmountTotal = 0, long producedNewBalance = 0,
            PlayerDoc? docAfter = null)
        {
            Code = code;
            ConsumedCount = consumedCount;
            HasProduce = hasProduce;
            ProducedType = producedType;
            ProducedAmountTotal = producedAmountTotal;
            ProducedNewBalance = producedNewBalance;
            DocAfter = docAfter;
        }
    }

    /// <summary>
    /// 使用道具专用事务。身份从会话取(handler 传入 accountId),reqSeq 幂等去重。
    /// 扣道具 + 产出货币折叠进同一条原子命令(单文档,真原子):
    ///   - 批次轨:读改写(FIFO 扣)+ InventoryVersion 乐观并发,写命令 Set 新批次数组 + $inc 货币 + $set 幂等锚/版本。
    ///   - 堆叠轨:单条 $inc 条件过滤扣减 + $inc 货币 + $set 幂等锚。
    /// </summary>
    public static async FTask<UseResult> UseItem(Scene scene, string accountId, int itemId, long count, long reqSeq)
    {
        if (itemId <= 0 || count <= 0 || reqSeq <= 0 || count > MaxUseCount)
        {
            return new UseResult(UseItemResultCode.InvalidRequest);
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return new UseResult(UseItemResultCode.ServiceUnavailable);
        }

        var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(itemId);
        if (def == null)
        {
            return new UseResult(UseItemResultCode.UnknownItem);
        }

        // 使用效果解析(本轮只支持货币):UseEffect==1 num → TbCurrency 映射 PropertyType。不可解析 → NotUsable。
        if (!TryResolveCurrencyProduce(def, out var produceType, out var producePerItem))
        {
            return new UseResult(UseItemResultCode.NotUsable);
        }

        long produceTotal = producePerItem * count;
        // 货币产出字段名 + 上界(与 ChangeProperty 同源),折叠 $inc 时做 filter 夹界。
        if (!PlayerPropertyServiceHelper.TryGetPropertyFieldMeta(service, produceType, out var curField, out var curUpper))
        {
            return new UseResult(UseItemResultCode.NotUsable);
        }
        if (produceTotal < 0 || produceTotal > curUpper)
        {
            // perItem*count 溢出货币上界:非法请求(正常配置 + 正常 count 不会触发)。
            return new UseResult(UseItemResultCode.InvalidRequest);
        }

        var nowMs = TimeHelper.Now;
        string consumeReason = $"item_use:item{itemId}:x{count}";
        string produceReason = $"item_use:item{itemId}:produce";

        // 限时道具走批次轨 FIFO;非限时走堆叠轨 $inc。
        return def.Term != 0
            ? await UseFromLots(service, players, accountId, itemId, count, reqSeq, nowMs,
                produceType, produceTotal, curField, curUpper, consumeReason, produceReason)
            : await UseFromHoldings(service, players, accountId, itemId, count, reqSeq, nowMs,
                produceType, produceTotal, curField, curUpper, consumeReason, produceReason);
    }

    /// <summary>堆叠轨使用:单条原子 $inc 扣减 + 货币折叠 + 幂等锚。</summary>
    private static async FTask<UseResult> UseFromHoldings(
        PlayerPropertyServiceComponent service, IMongoCollection<PlayerDoc> players,
        string accountId, int itemId, long count, long reqSeq, long nowMs,
        PropertyType produceType, long produceTotal, string curField, long curUpper,
        string consumeReason, string produceReason)
    {
        var holdField = "ItemHoldings." + itemId;
        // filter:账号 + 幂等锚(reqSeq 严格大于已处理) + 持有足额 + 货币不越上界。
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Lt(x => x.LastUseReqSeq, reqSeq),
            Builders<PlayerDoc>.Filter.Gte(holdField, count),
            Builders<PlayerDoc>.Filter.Lte(curField, curUpper - produceTotal));
        var update = Builders<PlayerDoc>.Update
            .Inc(holdField, -count)
            .Inc(curField, produceTotal)
            .Set(x => x.LastUseReqSeq, reqSeq)
            .Set(x => x.LastChangeUnixMs, nowMs);

        PlayerDoc? doc;
        try
        {
            doc = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"UseItem(holdings) CAS 写库失败 account={accountId} item={itemId},err={e.Message}");
            return new UseResult(UseItemResultCode.ServiceUnavailable);
        }

        if (doc != null)
        {
            long itemBalance = ItemHoldingsServiceHelper.ReadHolding(doc, itemId);
            long newCur = PlayerPropertyServiceHelper.ReadPropertyValue(doc, produceType);
            await WriteUseLedgers(service, accountId, itemId, count, itemBalance,
                produceType, produceTotal, newCur, nowMs, consumeReason, produceReason);
            return new UseResult(UseItemResultCode.Success, count, produceTotal > 0, produceType, produceTotal, newCur, doc);
        }

        // CAS 未命中:重读区分重复 / 不足 / 越界。
        var fresh = await ReadDoc(players, accountId);
        if (fresh == null)
        {
            return new UseResult(UseItemResultCode.ServiceUnavailable);
        }
        if (fresh.LastUseReqSeq >= reqSeq)
        {
            // 幂等重发:回带当前文档,handler 据此回推权威背包,纠正客户端可能的乐观偏差(首次成功但推送/ack 丢失)。
            return new UseResult(UseItemResultCode.Duplicate, docAfter: fresh);
        }
        if (ItemHoldingsServiceHelper.ReadHolding(fresh, itemId) < count)
        {
            return new UseResult(UseItemResultCode.NotEnough);
        }
        // 足额未重复却未命中:货币越上界(产出会溢出)或并发窗口 → 服务不可用让客户端重试。
        Log.Warning($"UseItem(holdings) CAS 未命中(货币越界/并发) account={accountId} item={itemId} count={count}");
        return new UseResult(UseItemResultCode.ServiceUnavailable);
    }

    /// <summary>批次轨使用:读改写(按 ExpireMs FIFO 扣未过期批次)+ InventoryVersion 乐观并发,货币折叠进写命令。</summary>
    private static async FTask<UseResult> UseFromLots(
        PlayerPropertyServiceComponent service, IMongoCollection<PlayerDoc> players,
        string accountId, int itemId, long count, long reqSeq, long nowMs,
        PropertyType produceType, long produceTotal, string curField, long curUpper,
        string consumeReason, string produceReason)
    {
        for (int attempt = 0; attempt < MaxCasRetry; attempt++)
        {
            var doc = await ReadDoc(players, accountId);
            if (doc == null)
            {
                return new UseResult(UseItemResultCode.ServiceUnavailable);
            }
            if (doc.LastUseReqSeq >= reqSeq)
            {
                // 幂等重发:回带当前文档供 handler 回推权威背包(纠正客户端乐观偏差)。
                return new UseResult(UseItemResultCode.Duplicate, docAfter: doc);
            }

            var lots = doc.ItemLots ?? new List<ItemLot>();
            // 分离:本道具未过期批次(可扣)/ 其余批次(其他道具 + 本道具已过期,原样保留)。
            var alive = new List<ItemLot>();
            var kept = new List<ItemLot>();
            long expiredCount = 0;
            foreach (var lot in lots)
            {
                if (lot.ItemId == itemId && !IsExpired(lot, nowMs))
                {
                    alive.Add(lot);
                }
                else
                {
                    if (lot.ItemId == itemId) expiredCount += lot.Count;
                    kept.Add(lot);
                }
            }

            long available = 0;
            foreach (var lot in alive) available += lot.Count;
            if (available < count)
            {
                // 未过期批次不足:若「未过期为 0 且存在过期批次足额」则语义是全过期,回 Expired 便于客户端提示。
                if (available == 0 && expiredCount >= count)
                {
                    return new UseResult(UseItemResultCode.Expired);
                }
                return new UseResult(UseItemResultCode.NotEnough);
            }

            // FIFO:按过期时刻升序扣减,扣空的批次丢弃。
            alive.Sort((a, b) => a.ExpireMs.CompareTo(b.ExpireMs));
            long remaining = count;
            var newLots = new List<ItemLot>(kept);
            foreach (var lot in alive)
            {
                if (remaining <= 0)
                {
                    newLots.Add(lot);
                    continue;
                }
                long take = lot.Count <= remaining ? lot.Count : remaining;
                lot.Count -= take;
                remaining -= take;
                if (lot.Count > 0) newLots.Add(lot);
            }

            long version = doc.InventoryVersion;
            // 写命令:版本一致 + 幂等锚 + 货币不越界 才落;Set 新批次数组 + $inc 货币 + 幂等锚/版本推进。
            var filter = Builders<PlayerDoc>.Filter.And(
                Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
                Builders<PlayerDoc>.Filter.Eq(x => x.InventoryVersion, version),
                Builders<PlayerDoc>.Filter.Lt(x => x.LastUseReqSeq, reqSeq),
                Builders<PlayerDoc>.Filter.Lte(curField, curUpper - produceTotal));
            var update = Builders<PlayerDoc>.Update
                .Set(x => x.ItemLots, newLots)
                .Inc(curField, produceTotal)
                .Set(x => x.LastUseReqSeq, reqSeq)
                .Set(x => x.InventoryVersion, version + 1)
                .Set(x => x.LastChangeUnixMs, nowMs);

            PlayerDoc? written;
            try
            {
                written = await players.FindOneAndUpdateAsync(filter, update,
                    new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            }
            catch (MongoException e)
            {
                Log.Warning($"UseItem(lots) 写库失败 account={accountId} item={itemId},err={e.Message}");
                return new UseResult(UseItemResultCode.ServiceUnavailable);
            }

            if (written != null)
            {
                long itemBalance = SumLotCount(written, itemId);
                long newCur = PlayerPropertyServiceHelper.ReadPropertyValue(written, produceType);
                await WriteUseLedgers(service, accountId, itemId, count, itemBalance,
                    produceType, produceTotal, newCur, nowMs, consumeReason, produceReason);
                return new UseResult(UseItemResultCode.Success, count, produceTotal > 0, produceType, produceTotal, newCur, written);
            }
            // 未命中:重读区分「幂等重发 / 货币越界 / 版本冲突」。
            var recheck = await ReadDoc(players, accountId);
            if (recheck != null)
            {
                if (recheck.LastUseReqSeq >= reqSeq)
                {
                    // 幂等重发:回带当前文档供 handler 回推权威背包。
                    return new UseResult(UseItemResultCode.Duplicate, docAfter: recheck);
                }
                // 货币产出会越上界:不随重试恢复(货币上界稳定),直接返服务不可用,避免空耗重试。
                // 此路径未扣道具(写命令整体未命中),道具不丢。
                if (PlayerPropertyServiceHelper.ReadPropertyValue(recheck, produceType) > curUpper - produceTotal)
                {
                    Log.Warning($"UseItem(lots) 产出货币越上界拒 account={accountId} item={itemId} produce={produceTotal} upper={curUpper}");
                    return new UseResult(UseItemResultCode.ServiceUnavailable);
                }
            }
            // 否则视为版本冲突,继续下一次重试。
        }

        Log.Warning($"UseItem(lots) 乐观并发重试耗尽 account={accountId} item={itemId} count={count}");
        return new UseResult(UseItemResultCode.ServiceUnavailable);
    }

    /// <summary>写使用事务的两条流水:道具消耗(-count)+ 货币产出(+produceTotal,仅 produceTotal>0)。旁路追加,失败仅告警不回滚。</summary>
    private static async FTask WriteUseLedgers(
        PlayerPropertyServiceComponent service, string accountId, int itemId, long count, long itemBalanceAfter,
        PropertyType produceType, long produceTotal, long produceNewBalance, long nowMs,
        string consumeReason, string produceReason)
    {
        await ItemHoldingsServiceHelper.AppendItemLedger(service, accountId, itemId, -count, itemBalanceAfter, consumeReason);
        if (produceTotal > 0)
        {
            await AttrLedgerHelper.AppendAsync(service, accountId, produceType,
                balanceBefore: produceNewBalance - produceTotal,
                balanceAfter: produceNewBalance,
                delta: produceTotal,
                reasonRaw: produceReason,
                timestampMs: nowMs);
        }
    }

    // ==================== 发放(限时道具批次) ====================

    /// <summary>
    /// 服务端权威发放限时道具:算绝对过期时刻(TbItemDef.Term/TermTime)→ 追加一条批次到 ItemLots。
    /// 仅限时道具(Term != 0)走本路径;非限时可堆叠道具走 ItemHoldingsServiceHelper.GrantItem。
    /// 返回 (ok, lotId):ok=false 时 lotId 空(入参非法 / 非限时 / 过期时刻配置非法 / MongoDB 不可达)。
    /// </summary>
    public static async FTask<(bool ok, string lotId)> GrantLot(
        PlayerPropertyServiceComponent service, string accountId, int itemId, long count, string source)
    {
        if (service?.Players is not { } players || string.IsNullOrEmpty(accountId) || itemId <= 0 || count <= 0)
        {
            return (false, string.Empty);
        }
        var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(itemId);
        if (def == null || def.Term == 0)
        {
            Log.Warning($"GrantLot 非限时道具或无配置 item={itemId} term={def?.Term};限时道具才走批次轨。");
            return (false, string.Empty);
        }

        var nowMs = TimeHelper.Now;
        if (!TryComputeExpireMs(def, nowMs, out var expireMs))
        {
            Log.Error($"GrantLot 过期时刻配置非法 item={itemId} term={def.Term} termTime='{def.TermTime}';拒发放。");
            return (false, string.Empty);
        }

        var lot = new ItemLot
        {
            LotId = ObjectId.GenerateNewId().ToString(),
            ItemId = itemId,
            Count = count,
            AcquireMs = nowMs,
            ExpireMs = expireMs,
            Source = source ?? string.Empty,
        };
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = Builders<PlayerDoc>.Update
            .Push(x => x.ItemLots, lot)
            .Inc(x => x.InventoryVersion, 1L);
        try
        {
            var doc = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (doc == null)
            {
                Log.Warning($"GrantLot 未命中玩家文档 account={accountId} item={itemId}");
                return (false, string.Empty);
            }
            long itemBalance = SumLotCount(doc, itemId);
            await ItemHoldingsServiceHelper.AppendItemLedger(service, accountId, itemId, count, itemBalance, $"item_grant_lot:{source}");
            return (true, lot.LotId);
        }
        catch (MongoException e)
        {
            Log.Warning($"GrantLot 写库失败 account={accountId} item={itemId} count={count},err={e.Message}");
            return (false, string.Empty);
        }
    }

    // ==================== 惰性过期(登录结算) ====================

    /// <summary>
    /// 登录时惰性过期结算:剔除已过期批次。
    ///   - 无补偿(TbItemDef.Compensate <= 0)的过期批次:删库 + 记流水(-count)。
    ///   - 有补偿的过期批次:本轮不删、不发补偿(补偿走 Reward 表 / 邮件,属后续接入),留库待结算,仅记日志。
    /// 快照下发与使用裁决都以 ExpireMs 现算过滤,故即便有补偿批次残留,玩家也看不到、用不了(不影响正确性)。
    /// 单条原子写(InventoryVersion 乐观),命中冲突则跳过(登录单飞,下次登录重试)。就地更新 doc.ItemLots 供随后快照使用。
    /// </summary>
    public static async FTask SettleExpiredLotsAtLogin(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        if (service?.Players is not { } players || doc?.ItemLots == null || doc.ItemLots.Count == 0)
        {
            return;
        }

        var survivors = new List<ItemLot>();
        var removed = new List<ItemLot>();
        int compensatePending = 0;
        foreach (var lot in doc.ItemLots)
        {
            if (!IsExpired(lot, nowMs))
            {
                survivors.Add(lot);
                continue;
            }
            var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(lot.ItemId);
            if (def != null && def.Compensate > 0)
            {
                // 有补偿:留库待后续结算(补偿子系统未接入),快照/使用仍按过期过滤不下发。
                survivors.Add(lot);
                compensatePending++;
            }
            else
            {
                removed.Add(lot);
            }
        }

        if (compensatePending > 0)
        {
            // 有补偿的过期批次留库待结算(补偿子系统未接入):留库避免毁玩家价值,但补偿接入前会随过期累积。
            // 用 Debug 避免每登录刷 Info 噪声(接入补偿结算后此分支即消解)。
            Log.Debug($"SettleExpiredLotsAtLogin 有补偿过期批次留库待结算(补偿子系统未接入) account={accountId} pending={compensatePending}");
        }
        if (removed.Count == 0)
        {
            return;
        }

        long version = doc.InventoryVersion;
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.InventoryVersion, version));
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.ItemLots, survivors)
            .Inc(x => x.InventoryVersion, 1L);
        try
        {
            var written = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (written == null)
            {
                // 版本冲突(并发登录/发放):本次不结算,下次登录重试。不阻断登录。
                return;
            }
            doc.ItemLots = written.ItemLots ?? survivors;
            doc.InventoryVersion = written.InventoryVersion;
            foreach (var lot in removed)
            {
                long itemBalance = SumLotCount(doc, lot.ItemId);
                await ItemHoldingsServiceHelper.AppendItemLedger(
                    service, accountId, lot.ItemId, -lot.Count, itemBalance, $"item_expire:lot{lot.LotId}");
            }
            Log.Info($"SettleExpiredLotsAtLogin 剔除无补偿过期批次 account={accountId} removed={removed.Count}");
        }
        catch (MongoException e)
        {
            Log.Warning($"SettleExpiredLotsAtLogin 写库失败(不阻断登录) account={accountId},err={e.Message}");
        }
    }

    // ==================== 快照 / 推送构建 ====================

    /// <summary>构建堆叠轨下发项(itemId → 数量)。缺省空字典 → 空列表。</summary>
    public static List<InventoryHolding> BuildHoldingMessages(PlayerDoc doc)
    {
        var list = new List<InventoryHolding>();
        if (doc?.ItemHoldings == null) return list;
        foreach (var kv in doc.ItemHoldings)
        {
            if (kv.Value == 0) continue; // 扣空的键不下发(客户端投影按缺省 0)。
            if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            var h = InventoryHolding.Create();
            h.ItemId = id;
            h.Count = kv.Value;
            list.Add(h);
        }
        return list;
    }

    /// <summary>构建批次轨下发项(已按 nowMs 剔除过期批次)。</summary>
    public static List<InventoryLot> BuildLotMessages(PlayerDoc doc, long nowMs)
    {
        var list = new List<InventoryLot>();
        if (doc?.ItemLots == null) return list;
        foreach (var lot in doc.ItemLots)
        {
            if (IsExpired(lot, nowMs)) continue; // 过期批次不下发(惰性过期:展示层现算过滤)。
            var m = InventoryLot.Create();
            m.LotId = lot.LotId;
            m.ItemId = lot.ItemId;
            m.Count = lot.Count;
            m.AcquireMs = lot.AcquireMs;
            m.ExpireMs = lot.ExpireMs;
            list.Add(m);
        }
        return list;
    }

    /// <summary>背包变更后推送整份当前背包到该 UUID 在线全部会话(绝对整份,客户端覆盖投影)。doc/离线/会话断 → 丢弃。</summary>
    public static void SendInventoryDeltaPush(Scene scene, string accountId, PlayerDoc? doc)
    {
        if (doc == null || !AccountManageHelper.TryGetAccount(scene, accountId, out var account))
        {
            return;
        }
        Session session = account.Session;
        if (session == null || session.IsDisposed)
        {
            return;
        }
        var nowMs = TimeHelper.Now;
        var push = new G2C_InventoryDeltaPush
        {
            ServerNowMs = nowMs,
            Loaded = true,
        };
        foreach (var h in BuildHoldingMessages(doc)) push.Holdings.Add(h);
        foreach (var m in BuildLotMessages(doc, nowMs)) push.Lots.Add(m);
        session.Send(push);
    }

    // ==================== GM 清背包 ====================

    /// <summary>
    /// GM 清背包(调试用):把背包两轨清空(堆叠轨 ItemHoldings + 批次轨 ItemLots)、使用幂等锚 LastUseReqSeq 归 0,
    /// InventoryVersion 乐观版本 +1(单调推进,使任何在途乐观写失配、下次重读;不 Set 0 以免与并发写 ABA)。
    /// 单条原子 FindOneAndUpdate($set 两轨 + 幂等锚、$inc 版本),整份清一次落库,不动货币 / 进度 / 其它字段。
    /// 幂等:重复清同样刷成空(版本仍 +1)。返回 (ok, doc):ok=true 且 doc 非空 → 清后文档供推快照;
    /// doc==null(账号未首登,无文档可清)视为已空的幂等成功;ok=false → 服务 / 库不可用。
    /// </summary>
    public static async FTask<(bool ok, PlayerDoc? doc)> ClearInventory(Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (false, null);
        }

        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.ItemHoldings, new Dictionary<string, long>())
            .Set(x => x.ItemLots, new List<ItemLot>())
            .Set(x => x.LastUseReqSeq, 0L)
            .Inc(x => x.InventoryVersion, 1L);

        try
        {
            var doc = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (doc == null)
            {
                // 账号未首登(理论上清背包前必已登录,防御性):无文档可清,等价已空,幂等成功但无 doc 可推。
                Log.Debug($"InventoryServiceHelper.ClearInventory: 玩家文档不存在,视为已空,account={accountId}。");
                return (true, null);
            }
            Log.Info($"InventoryServiceHelper.ClearInventory 清背包成功 account={accountId} version={doc.InventoryVersion}");
            return (true, doc);
        }
        catch (MongoException e)
        {
            Log.Warning($"InventoryServiceHelper.ClearInventory 失败 account={accountId},err={e.Message}");
            return (false, null);
        }
    }

    // ==================== GM 发测试道具 ====================

    /// <summary>GM 发测试道具单次数量上限(调试入口防误填天量;正常调试远不及)。</summary>
    private const long MaxGrantTestCount = 100_000L;

    /// <summary>
    /// GM 发背包测试道具(调试用):按 itemId 给指定账号发 count 个 —— 限时道具(TbItemDef.Term != 0)追加一条批次(GrantLot)、
    /// 可堆叠道具(Term == 0)累加持有(GrantItem)。开发者调试入口,只做基本合法性校验(itemId 存在 + count 有界),不设反作弊。
    /// 返回 (code, doc):code=Success 且 doc 非空 → 发后文档供 handler 推整份背包快照;失败时 doc 为 null。
    /// </summary>
    public static async FTask<(GrantTestItemResultCode code, PlayerDoc? doc)> GrantTestItem(
        Scene scene, string accountId, int itemId, long count)
    {
        if (itemId <= 0 || count <= 0 || count > MaxGrantTestCount)
        {
            return (GrantTestItemResultCode.InvalidRequest, null);
        }
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (GrantTestItemResultCode.ServiceUnavailable, null);
        }
        var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(itemId);
        if (def == null)
        {
            return (GrantTestItemResultCode.UnknownItem, null);
        }

        const string reason = "gm_grant_test_item";
        // 限时走批次轨(GrantLot 内含过期时刻计算),非限时走堆叠轨(GrantItem)。
        bool ok = def.Term != 0
            ? (await GrantLot(service, accountId, itemId, count, reason)).ok
            : (await ItemHoldingsServiceHelper.GrantItem(service, accountId, itemId, count, reason)).ok;
        if (!ok)
        {
            return (GrantTestItemResultCode.ServiceUnavailable, null);
        }

        var doc = await ReadDoc(players, accountId);
        return (GrantTestItemResultCode.Success, doc);
    }

    // ==================== 内部工具 ====================

    /// <summary>批次是否已过期(ExpireMs > 0 且 now >= ExpireMs;ExpireMs<=0 视为永不过期,批次轨一般不出现)。</summary>
    private static bool IsExpired(ItemLot lot, long nowMs) => lot.ExpireMs > 0 && nowMs >= lot.ExpireMs;

    /// <summary>汇总某道具在批次轨的当前总持有量(供流水 BalanceAfter)。</summary>
    private static long SumLotCount(PlayerDoc doc, int itemId)
    {
        long sum = 0;
        if (doc?.ItemLots == null) return 0;
        foreach (var lot in doc.ItemLots)
        {
            if (lot.ItemId == itemId) sum += lot.Count;
        }
        return sum;
    }

    private static async FTask<PlayerDoc?> ReadDoc(IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            return await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"InventoryServiceHelper.ReadDoc 失败 account={accountId},err={e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 算限时道具绝对过期时刻:
    ///   Term==1 指定日期:TermTime = 日期串(解析为 UTC),到该绝对时刻全服统一过期。
    ///   Term==2 指定时长:TermTime = 时长秒,过期时刻 = 获得时刻 + 时长(分批领取分批过期)。
    /// 解析失败返 false(调用方按配置非法拒发放)。
    /// </summary>
    private static bool TryComputeExpireMs(ItemDef def, long nowMs, out long expireMs)
    {
        expireMs = 0;
        var raw = def.TermTime ?? string.Empty;
        if (def.Term == 2)
        {
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            {
                return false;
            }
            expireMs = nowMs + seconds * 1000L;
            return true;
        }
        if (def.Term == 1)
        {
            if (!System.DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return false;
            }
            expireMs = new System.DateTimeOffset(dt, System.TimeSpan.Zero).ToUnixTimeMilliseconds();
            return expireMs > 0;
        }
        return false;
    }

    /// <summary>
    /// 解析货币使用效果(本轮唯一支持的效果):UseEffect==1 num → TbCurrency 映射 PropertyType,产出量 = UseNum(每个道具)。
    /// 不是货币效果 / num 无配置 / num 类型无对应服务端 PropertyType(如 EXP)/ UseNum<=0 → 返 false(NotUsable)。
    /// public:供发奖路径(如塔罗奖励 TarotRewardServiceHelper)对 automatic 货币道具「发放即转货币」复用同一 num→PropertyType 映射,不另写一份。
    /// </summary>
    public static bool TryResolveCurrencyProduce(ItemDef def, out PropertyType type, out long perItem)
    {
        type = default;
        perItem = 0;
        if (def.UseEffect != 1)
        {
            return false; // 非货币效果(图案 / 礼包)本轮不支持。
        }
        var cur = GameConfigSystem.Tables?.TbCurrency?.GetOrDefault(def.UseValue);
        if (cur == null)
        {
            return false;
        }
        if (!MapCurrencyTypeToProperty(cur.CurrencyType, out type))
        {
            return false;
        }
        if (def.UseNum <= 0)
        {
            return false;
        }
        perItem = def.UseNum;
        return true;
    }

    /// <summary>
    /// currency.ECurrencyType → 服务端 PropertyType。EXP(经验)无对应 PropertyType(PlayerDoc.Exp 不在 PropertyType 枚举)→ false。
    /// public:货币产出的单一映射源,供道具使用(TryResolveCurrencyProduce)与固定奖励盒发放(RewardBoxServiceHelper)共用,不另写一份。
    /// </summary>
    public static bool MapCurrencyTypeToProperty(GameConfig.currency.ECurrencyType currencyType, out PropertyType type)
    {
        switch (currencyType)
        {
            case GameConfig.currency.ECurrencyType.PIETY: type = PropertyType.Piety; return true;
            case GameConfig.currency.ECurrencyType.DIAMOND: type = PropertyType.Diamond; return true;
            case GameConfig.currency.ECurrencyType.ENERGY: type = PropertyType.Energy; return true;
            default: type = default; return false; // EXP 等:无对应 PropertyType。
        }
    }
}
