using Fantasy.Async;
using Fantasy.Helper;
using GameConfig;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 女神奖励「溢出进背包 / 双击取回」服务端权威裁决核心(元素道具是玩家资产,服务端 PlayerDoc.ItemHoldings 唯一权威)。
///
/// 背景:女神领取(GoddessClaimServiceHelper)只把奖励元素回带客户端;客户端把元素飞到盘面「有方块无元素」的格子,
/// 盘面装不下的溢出量客户端自算,经本 helper 入库(元素道具落堆叠轨 ItemHoldings);玩法界面双击背包元素道具「用掉一个」
/// 经本 helper 服务端 -1 后,客户端再把元素飞回盘面。元素道具 = itemdef 预置的 32101..32405(itemId = 32000 + 花色×100 + 等级,
/// 花色 1剑/2杯/3杖/4星币,等级 1..5;女神奖励全 Lv1 故实际只涉及 32x01),纯持有可堆叠(Term=0 → 堆叠轨)。
///
/// 反作弊红线(元素道具属玩家资产,客户端可伪造上报):
///   - itemId 必须属元素道具集(<see cref="IsElementItem"/> + TbItemDef 存在),否则拒——防客户端借溢出 RPC 白发货币/其它道具。
///   - 溢出绑死到真实领取:领取(GoddessClaimServiceHelper.TryClaim)签发一次性令牌(PlayerDoc.PendingOverflowNonce = nowMs,
///     PendingOverflowBudget = 奖励总量);溢出请求带 nonce,原子校验 nonce 匹配 + 剩余预算 >= count 才落账并扣预算。
///     无真实领取 → 无有效 nonce → 溢出拒;单次领取的溢出总量被预算封顶——堵住「不领奖、反复发溢出 RPC」的 RPC 刷取。
///   - reqSeq 请求级幂等(复用 PlayerDoc.LastUseReqSeq 单调锚,与 UseItem 同一序列):防弱网重发双发(入库)/ 双扣(取回)。
///     客户端须对全部背包变更 RPC(UseItem / 溢出 / 取回)共用一个单调递增 reqSeq。
///
/// 落账 = 单文档原子 FindOneAndUpdate(与 InventoryServiceHelper.UseFromHoldings 同范式);写库成功后旁路记 player_item_ledger。
/// 幂等重发一律回 Success + 当前权威持有量(操作已发生,对客户端等价成功),并回带 docAfter 供 handler 回推权威背包。
/// </summary>
public static class GoddessBagServiceHelper
{
    /// <summary>元素道具入库/取回裁决结果(handler 据此回响应 + 起推送)。</summary>
    public readonly struct BagResult
    {
        public readonly GoddessBagResultCode Code;
        public readonly long NewCount;
        /// <summary>成功/幂等时的变更后文档(供构建背包推送);失败为 null。</summary>
        public readonly PlayerDoc? DocAfter;

        public BagResult(GoddessBagResultCode code, long newCount = 0, PlayerDoc? docAfter = null)
        {
            Code = code;
            NewCount = newCount;
            DocAfter = docAfter;
        }
    }

    /// <summary>元素道具 id 段:32101..32405(itemId = 32000 + 花色×100 + 等级,花色 1..4,等级 1..5)。</summary>
    public static bool IsElementItem(int itemId)
    {
        if (itemId < 32101 || itemId > 32405)
        {
            return false;
        }
        int suit = (itemId - 32000) / 100;   // 1..4
        int level = (itemId - 32000) % 100;  // 1..5
        if (suit < 1 || suit > 4 || level < 1 || level > 5)
        {
            return false;
        }
        // 配置存在性(防表内该行被删):元素道具须有 TbItemDef 行。
        return GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(itemId) != null;
    }

    /// <summary>
    /// 溢出入库:元素道具 itemId × count 幂等累加到堆叠轨 ItemHoldings,并扣减领取令牌预算。
    /// 校验 itemId ∈ 元素道具集 + nonce 匹配当前令牌 + 剩余预算 ≥ count + reqSeq 幂等——把溢出绑死到真实领取。
    /// </summary>
    public static async FTask<BagResult> GrantOverflow(Scene scene, string accountId, int itemId, long count, long reqSeq, long nonce)
    {
        if (count <= 0 || reqSeq <= 0 || nonce <= 0 || !IsElementItem(itemId))
        {
            return new BagResult(GoddessBagResultCode.InvalidItem);
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }

        var nowMs = TimeHelper.Now;
        var holdField = "ItemHoldings." + itemId;
        // filter:账号 + 幂等锚 + 令牌 nonce 匹配 + 剩余预算足额。命中即 $inc 累加持有 + $inc 扣预算 + 推进锚。
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Lt(x => x.LastUseReqSeq, reqSeq),
            Builders<PlayerDoc>.Filter.Eq(x => x.PendingOverflowNonce, nonce),
            Builders<PlayerDoc>.Filter.Gte(x => x.PendingOverflowBudget, count));
        var update = Builders<PlayerDoc>.Update
            .Inc(holdField, count)
            .Inc(x => x.PendingOverflowBudget, -count)
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
            Log.Warning($"GoddessBagServiceHelper.GrantOverflow 写库失败 account={accountId} item={itemId},err={e.Message}");
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }

        if (doc != null)
        {
            long after = ItemHoldingsServiceHelper.ReadHolding(doc, itemId);
            await ItemHoldingsServiceHelper.AppendItemLedger(service, accountId, itemId, count, after, $"goddess-overflow:x{count}");
            return new BagResult(GoddessBagResultCode.Success, after, doc);
        }

        // CAS 未命中:重读区分幂等重发 / 无效令牌(nonce 不匹配或预算不足)/ 账号缺失。
        var fresh = await ReadDoc(players, accountId);
        if (fresh == null)
        {
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }
        if (fresh.LastUseReqSeq >= reqSeq)
        {
            // 幂等重发(首次已入库、推送/ack 丢失):回 Success + 当前持有 + docAfter 供回推,不重复 $inc / 不重复扣预算。
            return new BagResult(GoddessBagResultCode.Success, ItemHoldingsServiceHelper.ReadHolding(fresh, itemId), fresh);
        }
        // reqSeq 未处理却未命中 = nonce 不匹配(无/过期令牌)或剩余预算 < count:无有效领取令牌,拒(反作弊)。
        return new BagResult(GoddessBagResultCode.InvalidItem);
    }

    /// <summary>
    /// 取回消耗:元素道具 itemId 幂等 -1(玩法界面双击「用掉一个」)。
    /// 校验 itemId ∈ 元素道具集 + 持有 ≥ 1 + reqSeq 幂等;成功后客户端再把元素飞回盘面。
    /// </summary>
    public static async FTask<BagResult> RetrieveElement(Scene scene, string accountId, int itemId, long reqSeq)
    {
        if (reqSeq <= 0 || !IsElementItem(itemId))
        {
            return new BagResult(GoddessBagResultCode.InvalidItem);
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }

        var nowMs = TimeHelper.Now;
        var holdField = "ItemHoldings." + itemId;
        // filter:账号 + 幂等锚 + 持有足额(≥1)。命中即 $inc -1 + 推进锚。
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Lt(x => x.LastUseReqSeq, reqSeq),
            Builders<PlayerDoc>.Filter.Gte(holdField, 1L));
        var update = Builders<PlayerDoc>.Update
            .Inc(holdField, -1L)
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
            Log.Warning($"GoddessBagServiceHelper.RetrieveElement 写库失败 account={accountId} item={itemId},err={e.Message}");
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }

        if (doc != null)
        {
            long after = ItemHoldingsServiceHelper.ReadHolding(doc, itemId);
            await ItemHoldingsServiceHelper.AppendItemLedger(service, accountId, itemId, -1L, after, "goddess-retrieve");
            return new BagResult(GoddessBagResultCode.Success, after, doc);
        }

        // CAS 未命中:重读区分幂等重发 / 持有不足 / 账号缺失。
        var fresh = await ReadDoc(players, accountId);
        if (fresh == null)
        {
            return new BagResult(GoddessBagResultCode.ServiceUnavailable);
        }
        if (fresh.LastUseReqSeq >= reqSeq)
        {
            // 幂等重发(首次已扣):回 Success + 当前持有 + docAfter 供回推,不重复 $inc。
            return new BagResult(GoddessBagResultCode.Success, ItemHoldingsServiceHelper.ReadHolding(fresh, itemId), fresh);
        }
        if (ItemHoldingsServiceHelper.ReadHolding(fresh, itemId) < 1L)
        {
            // 库存为 0:回 NotEnough + 当前持有(0)。
            return new BagResult(GoddessBagResultCode.NotEnough, ItemHoldingsServiceHelper.ReadHolding(fresh, itemId));
        }
        return new BagResult(GoddessBagResultCode.ServiceUnavailable);
    }

    private static async FTask<PlayerDoc?> ReadDoc(IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            return await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"GoddessBagServiceHelper.ReadDoc 失败 account={accountId},err={e.Message}");
            return null;
        }
    }
}
