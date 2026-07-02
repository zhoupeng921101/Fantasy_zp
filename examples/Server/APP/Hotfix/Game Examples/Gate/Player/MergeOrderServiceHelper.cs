using System;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 订单系统服务端权威裁决核心(P2 全栈迁移·Phase 1·normal 订单)。
///
/// 三类对外能力,共用同一组持久状态(PlayerDoc.OrderCursor / LastOrderRefreshMs / OrderDeliveredMask):
///   1. ApplyOrderRefreshIfDue — 登录拉快照前 + 每次交付前调:按 nowMs - LastOrderRefreshMs 是否 ≥ 间隔
///      判定刷新整批(类比 Energy 懒结算);首次/负时差/离线只一批不堆叠(同客户端 ApplyOrderRefresh 语义)。
///   2. BuildSnapshot — 由 doc 当前 cursor + mask 派生「激活订单快照」(协议层 MergeOrderSnapshot),
///      已交付槽以 Type=0 空槽占位,客户端整份覆盖本地视图。
///   3. TryDeliver — 交付 RPC 内部裁决:刷新结算 → claim-then-act:CAS 抢占 mask 槽位 → 抢占成功才发奖
///      (Energy + Piety 经 PlayerPropertyServiceHelper.ChangeProperty serverAuthoritative=true 入口落账
///      + ledger + delta 推送;塔罗碎片经 ItemHoldingsServiceHelper.GrantItem 落持有字典 + item ledger)
///      → 返回最新快照。库存校验本轮不做(farming 已由 mask 一次性抢占堵死)。
///
/// 反作弊红线:
///   - 交付奖励金额 = 服务端按 OrderCursor + 槽位查 TbMergeOrder 表行自己算定(体力/虔诚币/碎片直配值),
///     **不**接受客户端上报金额;
///   - 幂等顺序 claim-then-act(沿 RankSettleHelper / ActivityEvalHelper 同范式):
///     ① 先 CAS 抢占 mask(filter 含 cursor + lastRefreshMs + bit 未置)→ MongoDB 单条原子保证每批每槽仅一次抢成;
///     ② 抢占成功才发奖,失败 → 重读判 AlreadyDelivered(或刷新已发生 → 按新状态返)。
///     这一顺序结构性堵死「并发同账号同槽双发」,不依赖频率闸偶然兜底。
///     代价:抢占成功后、发奖失败(MongoDB 抖动罕见)→ 槽已消耗但少这次奖,
///     anti-farming 语境下安全方向(玩家少拿、绝不多拿),记 Warning,不回滚 mask、不重复发。
///   - 奖励经 ChangeProperty(serverAuthoritative=true)走「上界 cap + 原子写 + ledger + 推送」,
///     跳过客户端 RPC 路径的单笔上限 + 100ms 频率闸(本侧 Energy + Piety 一笔内连续发、不同槽连交两单也不该被误拒)。
///
/// 时钟:统一用 TimeHelper.Now(服务端 Unix 毫秒);客户端时钟篡改影响不到本侧。
/// </summary>
public static class MergeOrderServiceHelper
{
    /// <summary>
    /// 按真实时差判定并执行订单整批刷新(服务端懒结算,语义对齐客户端 ApplyOrderRefresh)。
    /// 行为:
    ///   ① doc.LastOrderRefreshMs == 0 → 首次接触(旧档 / 首登)→ CAS 写入 nowMs、本次不刷、cursor 不动、mask 不动;
    ///   ② elapsed = nowMs - lastMs ≤ 0 → 不刷(同刻 / 负时差,服务端时钟不会负,防御);
    ///   ③ elapsed < OrderRefreshIntervalMs → 不刷;
    ///   ④ elapsed ≥ OrderRefreshIntervalMs → 整批刷新:cursor += ActiveOrders + mask = 0 + lastMs = nowMs(对齐到此刻,
    ///      离线很久也只刷到「最新一批」、不堆叠 N 批,同客户端语义)。
    ///
    /// CAS:filter 含 (AccountId, LastOrderRefreshMs == lastMs) 防并发双刷新。
    /// 写库失败 / 并发被另一路抢先刷新 → 不抛、Warning 不阻断,doc 字段保留 fast path 读到的值(下次操作前会重新读)。
    /// 成功后会把更新结果写回入参 doc 引用,调用方据此构造快照。
    /// </summary>
    public static async FTask ApplyOrderRefreshIfDue(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        var players = service.Players;
        if (players == null) return;

        var lastMs = doc.LastOrderRefreshMs;

        // 边界:旧档缺字段 / 首登(setOnInsert 没写过) → 0L。bootstrap 为 nowMs,本次不刷。
        if (lastMs <= 0L)
        {
            var bootstrapFilter = Builders<PlayerDoc>.Filter.And(
                Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
                Builders<PlayerDoc>.Filter.Eq(x => x.LastOrderRefreshMs, 0L));
            var bootstrapUpdate = Builders<PlayerDoc>.Update.Set(x => x.LastOrderRefreshMs, nowMs);
            try
            {
                var newDoc = await players.FindOneAndUpdateAsync(bootstrapFilter, bootstrapUpdate,
                    new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
                if (newDoc != null)
                {
                    doc.LastOrderRefreshMs = newDoc.LastOrderRefreshMs;
                    doc.OrderCursor = newDoc.OrderCursor;
                    doc.OrderDeliveredMask = newDoc.OrderDeliveredMask;
                    Log.Debug($"MergeOrder bootstrap account={accountId} lastRefreshMs={newDoc.LastOrderRefreshMs} cursor={newDoc.OrderCursor}");
                }
            }
            catch (MongoException e)
            {
                Log.Warning($"MergeOrderServiceHelper.ApplyOrderRefreshIfDue bootstrap 失败 account={accountId},err={e.Message}");
            }
            return;
        }

        var elapsed = nowMs - lastMs;
        if (elapsed < MergeOrderConfigServer.OrderRefreshIntervalMs) return;

        // 到点:整批刷新(行为同 reason="due")。CAS 键 LastOrderRefreshMs==lastMs 防并发双刷。
        await ApplyBatchRefreshWrite(players, accountId, doc, lastMs, nowMs, "due");
    }

    /// <summary>
    /// 整批刷新的 CAS 写库(时基刷新 + 全交付即刷共用,避免两处 CAS 逻辑重复后各自漂移)。
    /// 语义:cursor += ActiveOrders(下一批)+ OrderDeliveredMask = 0(新批全未交付)+ LastOrderRefreshMs = nowMs(对齐此刻)。
    ///
    /// CAS filter 键 = (AccountId, LastOrderRefreshMs == expectedLastMs):
    ///   并发多路(时基刷新 / 全交付即刷)只一路命中、把 LastOrderRefreshMs 改走,其余路 filter miss → updatedDoc=null,
    ///   结构性保证每批仅刷一次、不双进一批,不依赖外层判定顺序兜底。
    /// 成功命中 → 把更新结果写回入参 doc 引用,供调用方据此构造快照。
    /// 写库失败 → 沿用 catch(MongoException) → Warning、不抛、不阻断(doc 保留调用方读到的值,下次操作前重读重判)。
    /// </summary>
    private static async FTask ApplyBatchRefreshWrite(
        IMongoCollection<PlayerDoc> players, string accountId, PlayerDoc doc, long expectedLastMs, long nowMs, string reason)
    {
        // cursor 推进 ActiveOrders(让下一批指向 pool[cursor+ActiveOrders..cursor+2*ActiveOrders);等价于客户端 RefreshAllOrders 每槽 NextOrder() 累计推进 ActiveOrders 次)。
        var newCursor = doc.OrderCursor + MergeOrderConfigServer.ActiveOrders;
        var refreshFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.LastOrderRefreshMs, expectedLastMs));
        var refreshUpdate = Builders<PlayerDoc>.Update
            .Set(x => x.OrderCursor, newCursor)
            .Set(x => x.OrderDeliveredMask, 0)
            .Set(x => x.LastOrderRefreshMs, nowMs);
        try
        {
            var newDoc = await players.FindOneAndUpdateAsync(refreshFilter, refreshUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (newDoc != null)
            {
                doc.OrderCursor = newDoc.OrderCursor;
                doc.OrderDeliveredMask = newDoc.OrderDeliveredMask;
                doc.LastOrderRefreshMs = newDoc.LastOrderRefreshMs;
                Log.Debug($"MergeOrder 整批刷新({reason}) account={accountId} cursor={newDoc.OrderCursor} lastRefreshMs={newDoc.LastOrderRefreshMs}");
            }
            // newDoc == null:并发已被另一路刷新,本路当作已完成(下次操作前重读 doc)。
        }
        catch (MongoException e)
        {
            Log.Warning($"MergeOrderServiceHelper.ApplyBatchRefreshWrite({reason}) 刷新失败 account={accountId},err={e.Message}");
        }
    }

    /// <summary>
    /// 由 PlayerDoc 当前订单进度构造协议层快照(MergeOrderSnapshot)。
    /// 派生算式:激活订单第 i 槽 = OrderPool[(cursor + i) mod length],已交付的槽(mask bit i 置位)以 Type=0 空槽占位。
    /// 每槽附带奖励展示投影(体力/虔诚币/碎片,读 TbMergeOrder 表行),客户端只显示、实发以交付响应为准。
    /// 订单池表缺失 → 全空槽占位(可见降级)。首登 cursor==0 → 激活订单 = pool[0..ActiveOrders)。
    /// </summary>
    public static MergeOrderSnapshot BuildSnapshot(PlayerDoc doc)
    {
        var snap = MergeOrderSnapshot.Create();
        snap.OrderCursor = doc.OrderCursor;
        snap.LastOrderRefreshMs = doc.LastOrderRefreshMs;
        snap.OrderRefreshIntervalSec = MergeOrderConfigServer.OrderRefreshIntervalSec;

        for (int slot = 0; slot < MergeOrderConfigServer.ActiveOrders; slot++)
        {
            var item = OrderItem.Create();
            var entry = MergeOrderConfigServer.GetActiveOrder(doc.OrderCursor, slot);
            // 该槽已交付 / 订单池不可用 → Type=0 空槽占位(int32/int64 字段默认 0,无需显式清)
            if (entry != null && (doc.OrderDeliveredMask & (1 << slot)) == 0)
            {
                item.Type = entry.ElementType;
                item.Level = entry.Level;
                item.Count = entry.Count;
                item.EnergyReward = entry.EnergyReward;
                item.PietyReward = entry.PietyReward;
                item.FragmentItemId = entry.FragmentItemId;
                item.FragmentCount = entry.FragmentCount;
            }
            else
            {
                item.Type = MergeOrderConfigServer.OrderTypeNone;
            }
            snap.ActiveOrders.Add(item);
        }
        return snap;
    }

    /// <summary>
    /// 交付订单裁决(claim-then-act 幂等):刷新结算 → CAS 抢占 mask → 抢占成功才发奖 → 回传最新快照。
    ///
    /// 抢占在发奖前(范式同 RankSettleHelper.TryClaimSettlePeriod / ActivityEvalHelper.TryClaimCycleKey):
    /// 并发同账号同槽多路到达,MongoDB FindOneAndUpdate 原子保证只一路 filter 命中(bit 未置)、其他路返 null;
    /// 每个 (cursor 批, slot) 恰好抢占成功一次 = 恰好发奖一次,结构性堵死「双发」。
    /// 抢失败的路重读 doc:bit 已置 → AlreadyDelivered(并发对手已交付);cursor/lastMs 变了 → 期间已刷新一批,
    /// 重读后 mask 通常为 0、bit 也未置 → 此时按未交付返(刷新后的新状态),让客户端整份覆盖快照对齐。
    ///
    /// 返回 (resultCode, energyReward, pietyReward, energyBalance, pietyBalance,
    ///        fragmentItemId, fragmentReward, fragmentBalance, snapshot)。
    /// energyBalance / pietyBalance / fragmentBalance = 交付后权威绝对余额(回带响应供客户端对账,取代对发起会话的自推);
    /// -1 = 哨兵(本次未取到该项权威值,客户端不据此 set,靠快照/其它会话推送对齐)。
    /// fragmentItemId 仅在本次真发碎片(fragmentReward > 0)时非 0。
    /// 失败时 snapshot 尽量构造当前状态;ServiceUnavailable 时若 doc 都读不到,snapshot 为零长度 ActiveOrders 占位。
    /// </summary>
    public static async FTask<(DeliverOrderResultCode resultCode, long energyReward, long pietyReward, long energyBalance, long pietyBalance, int fragmentItemId, long fragmentReward, long fragmentBalance, MergeOrderSnapshot snapshot)>
        TryDeliver(Session session, string accountId, int slot)
    {
        // scene 由发起会话派生;session 供除冗自推(发奖 delta-push 经 SendDeltaPushToExcept 排除发起方,它已从响应绝对余额对账)。
        var scene = session.Scene;
        // 交付后 Energy / Piety / 碎片权威绝对余额,回带响应。-1 = 哨兵(该项未取到权威值,客户端不据此 set)。
        long energyBalance = -1L;
        long pietyBalance = -1L;
        long fragmentBalance = -1L;

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return (DeliverOrderResultCode.ServiceUnavailable, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, MergeOrderSnapshot.Create());
        }

        // 槽位合法性:协议层基础校验。
        if (slot < 0 || slot >= MergeOrderConfigServer.ActiveOrders)
        {
            var snap = await TryBuildSnapshotFromDb(service, accountId);
            return (DeliverOrderResultCode.InvalidSlot, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, snap);
        }

        var players = service.Players;
        var nowMs = TimeHelper.Now;

        // 读 doc + 跑刷新结算(到点会推进 cursor / 清 mask)。
        PlayerDoc doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"MergeOrderServiceHelper.TryDeliver 读 doc 失败 account={accountId},err={e.Message}");
            return (DeliverOrderResultCode.ServiceUnavailable, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, MergeOrderSnapshot.Create());
        }
        if (doc == null)
        {
            // 未首登(理论上登录链路已保证;实战防御)。
            return (DeliverOrderResultCode.ServiceUnavailable, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, MergeOrderSnapshot.Create());
        }

        await ApplyOrderRefreshIfDue(service, accountId, doc, nowMs);

        // 刷新结算后再校验 mask(刷新成功的话 mask 已清零)。
        var bit = 1 << slot;
        if ((doc.OrderDeliveredMask & bit) != 0)
        {
            // 本轮该槽已交付。
            return (DeliverOrderResultCode.AlreadyDelivered, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(doc));
        }

        // 首登 cursor=0 + LastOrderRefreshMs 刚 bootstrap 为 nowMs 时,激活订单 = pool[0..ActiveOrders)
        // (与 BuildSnapshot 派生口径一致,首登可立刻交付前 ActiveOrders 张)。
        var entry = MergeOrderConfigServer.GetActiveOrder(doc.OrderCursor, slot);
        if (entry == null)
        {
            // 订单池表缺失 / 空(导表遗漏等部署问题):可见降级,不静默按旧常量发奖。
            Log.Warning($"MergeOrderServiceHelper.TryDeliver 订单池不可用(TbMergeOrder 缺失/空) account={accountId} slot={slot}");
            return (DeliverOrderResultCode.ServiceUnavailable, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(doc));
        }

        // ── claim:先 CAS 抢占 mask 槽位 ──────────────────────────
        // filter 含「mask 该位未置 + cursor 未变 + lastRefreshMs 未变」:
        //   ·并发同槽多路 → MongoDB 原子保证只一路命中;其余路 filter 不匹配 → updatedDoc=null;
        //   ·期间发生刷新(cursor/lastMs 变了)→ 本路 filter 不匹配 → 重读判定。
        var newMask = doc.OrderDeliveredMask | bit;
        var maskFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.OrderCursor, doc.OrderCursor),
            Builders<PlayerDoc>.Filter.Eq(x => x.LastOrderRefreshMs, doc.LastOrderRefreshMs),
            Builders<PlayerDoc>.Filter.BitsAllClear(x => x.OrderDeliveredMask, (long)bit));
        var maskUpdate = Builders<PlayerDoc>.Update.Set(x => x.OrderDeliveredMask, newMask);

        PlayerDoc? claimedDoc;
        try
        {
            claimedDoc = await players.FindOneAndUpdateAsync(maskFilter, maskUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"DeliverOrder 抢占 mask 失败 account={accountId} slot={slot},err={e.Message}");
            return (DeliverOrderResultCode.ServiceUnavailable, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(doc));
        }

        if (claimedDoc == null)
        {
            // 抢占失败:并发对手已交付该槽,或刷新已发生 cursor/lastMs 变了。重读 doc 判定。
            PlayerDoc? freshDoc = null;
            try
            {
                freshDoc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            }
            catch (MongoException e)
            {
                Log.Warning($"DeliverOrder 抢占失败后重读 doc 失败 account={accountId} slot={slot},err={e.Message}");
            }
            if (freshDoc == null)
            {
                // 重读失败:沿用 fast path doc 给客户端最新视图,语义按 AlreadyDelivered 返(不发奖)。
                Log.Warning($"DeliverOrder 抢占未命中(并发交付该槽 / 刷新已发生),重读 doc 失败 account={accountId} slot={slot}");
                return (DeliverOrderResultCode.AlreadyDelivered, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(doc));
            }
            Log.Debug($"DeliverOrder 抢占未命中(并发对手已交付 / 刷新已发生) account={accountId} slot={slot} freshCursor={freshDoc.OrderCursor} freshMask={freshDoc.OrderDeliveredMask}");
            // 不发奖,以重读后状态构造快照。
            return (DeliverOrderResultCode.AlreadyDelivered, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(freshDoc));
        }

        // ── 抢占成功:同步 doc 引用为最新状态,准备发奖 ───────────
        doc.OrderDeliveredMask = claimedDoc.OrderDeliveredMask;
        doc.OrderCursor = claimedDoc.OrderCursor;
        doc.LastOrderRefreshMs = claimedDoc.LastOrderRefreshMs;

        // 计算奖励:全部读 TbMergeOrder 表行直配值(服务端按表自算,不接客户端上报)。
        long energyDelta = entry.EnergyReward;
        long pietyDelta = entry.PietyReward;
        var reason = $"merge_order_deliver:slot{slot}:t{entry.ElementType}l{entry.Level}c{entry.Count}";

        // ── act:服务端权威发奖。serverAuthoritative=true 跳过客户端 RPC 路径的单笔上限 + 100ms 频率闸,
        // 但仍走上界 cap + ledger(权威性不降)。落账后余额回带响应绝对余额供发起方对账;delta 推送经
        // SendDeltaPushToExcept 排除发起会话(除冗余自推),只对该账号其它在线会话对齐。
        // 发奖失败的安全方向:槽已消耗但少这次奖(玩家少拿、绝不多拿),记 Warning,不回滚 mask、不重复发。
        // ChangeProperty 是 all-or-nothing 落 delta:Success 时净增 = delta(无部分钳止);OverLimit 时整笔被拒、余额不动。
        var (energyResultCode, energyAfterAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Energy, energyDelta, reason, serverAuthoritative: true);

        long energyReward = 0L;
        if (energyResultCode == PropertyChangeResultCode.Success)
        {
            // 落账后余额 = 权威绝对值:回带响应对账,并对其它会话推送(排除发起方)。
            energyBalance = energyAfterAmount;
            PlayerPropertyServiceHelper.SendDeltaPushToExcept(scene, accountId, session, PropertyType.Energy, energyAfterAmount, reason);
            energyReward = energyDelta;
        }
        else if (energyResultCode == PropertyChangeResultCode.OverLimit)
        {
            // 体力撞 EnergyUpperBound 硬顶(9999,仅作 sanity 天花板)。规则上交付奖励允许超被动恢复软上限(30),
            // 走 serverAuthoritative=true 路径不受 SoftCap 与 SingleDeltaLimit 钳制;只有撞 9999 才进本分支。
            // 真业务量级远到不了 9999,本分支是兜底:跳过 Energy 发奖,继续 Piety + 维持 mask 已置(订单已消耗)。
            // energyAfterAmount 为当前权威余额(未变),仍回带响应供对账;OverLimit 未真发奖 → 不推送。
            energyBalance = energyAfterAmount;
            Log.Warning($"DeliverOrder Energy 钳硬顶 account={accountId} slot={slot} delta={energyDelta} currentEnergy={energyAfterAmount} reason='{reason}' → 跳过体力发奖,继续发 Piety");
            energyReward = 0L;
        }
        else
        {
            // 发奖失败(ServiceUnavailable / UnknownType / InvalidRequest;NotEnough 不可能因 delta>0):
            // 安全方向 = 槽已消耗、少这次 Energy + Piety / 碎片也不再尝试(避免半笔账)。
            // energyBalance 保持 -1 哨兵(未取到权威值):客户端 Success 分支据哨兵不 set 体力,靠快照/其它会话推送对齐。
            Log.Warning($"DeliverOrder Energy 发放失败(mask 已抢占) account={accountId} slot={slot} result={energyResultCode} reason='{reason}'");
            return (DeliverOrderResultCode.Success, 0L, 0L, energyBalance, pietyBalance, 0, 0L, fragmentBalance, BuildSnapshot(doc));
        }

        var (pietyResultCode, pietyAfterAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Piety, pietyDelta, reason, serverAuthoritative: true);

        long pietyReward = 0L;
        if (pietyResultCode == PropertyChangeResultCode.Success)
        {
            pietyBalance = pietyAfterAmount;
            PlayerPropertyServiceHelper.SendDeltaPushToExcept(scene, accountId, session, PropertyType.Piety, pietyAfterAmount, reason);
            pietyReward = pietyDelta;
        }
        else if (pietyResultCode == PropertyChangeResultCode.OverLimit)
        {
            // Piety 上界 1_000_000,实战不可能触顶;真触顶按同口径丢这笔。pietyAfterAmount 为当前权威余额,仍回带对账;未真发奖不推送。
            pietyBalance = pietyAfterAmount;
            Log.Warning($"DeliverOrder Piety 钳上界 account={accountId} slot={slot} delta={pietyDelta} currentPiety={pietyAfterAmount} reason='{reason}' → 跳过虔诚币发奖");
            pietyReward = 0L;
        }
        else
        {
            // Piety 发奖失败但 Energy 已成功:槽已消耗(mask 抢占在前),Piety 缺一笔由用户/运营事后补。
            // 不回滚 Energy:严格事务需要 MongoDB 多文档事务或 outbox,本子单不引入。
            // pietyBalance 保持 -1 哨兵:客户端不 set 虔诚币,靠快照/其它会话推送对齐(Energy 已从 energyBalance 对上)。
            Log.Warning($"DeliverOrder Piety 发放失败(Energy 已成功 / mask 已抢占) account={accountId} slot={slot} result={pietyResultCode} reason='{reason}'");
        }

        // ── 塔罗碎片(按表配置;失败口径同 Piety:少这笔、不回滚、记 Warning)──────────
        // 牌已集齐的碎片照发(无特殊分支):溢出碎片留在持有字典,图鉴显示「已集齐」。
        int fragmentItemId = 0;
        long fragmentReward = 0L;
        if (entry.FragmentItemId > 0 && entry.FragmentCount > 0)
        {
            var (fragOk, fragAfter) = await ItemHoldingsServiceHelper.GrantItem(
                service, accountId, entry.FragmentItemId, entry.FragmentCount, reason);
            if (fragOk)
            {
                fragmentItemId = entry.FragmentItemId;
                fragmentReward = entry.FragmentCount;
                fragmentBalance = fragAfter;
            }
            else
            {
                // fragmentBalance 保持 -1 哨兵:客户端不 set 本地计数,靠下次 EnterMainGame 快照对齐。
                Log.Warning($"DeliverOrder 碎片发放失败(mask 已抢占) account={accountId} slot={slot} fragItem={entry.FragmentItemId} count={entry.FragmentCount} reason='{reason}'");
            }
        }

        // ── 全交付即刷(触发器 ②):本批 ActiveOrders 槽全部交付 → 立即整批刷新一批、重置倒计时 ──
        // 不变量(时序无双刷):line 199 的时基刷新若已给新批,则本次 claim 落在新批、mask 只置一位不满,
        //   此分支条件 (mask & fullMask) == fullMask 不成立、不触发;故无需对「时基刷新与全交付即刷同帧」额外防护。
        // 并发双刷由 ApplyBatchRefreshWrite 的 CAS 键 LastOrderRefreshMs == doc.LastOrderRefreshMs 堵死:
        //   时基刷新若已占先改走 LastOrderRefreshMs,本次 CAS 自然 miss、不会双进一批。
        // doc.OrderDeliveredMask 已在抢占成功后同步为 claimedDoc 的值(含本次置位)。
        var fullMask = (1 << MergeOrderConfigServer.ActiveOrders) - 1;
        if ((doc.OrderDeliveredMask & fullMask) == fullMask)
        {
            await ApplyBatchRefreshWrite(players, accountId, doc, doc.LastOrderRefreshMs, nowMs, "all-delivered");
        }

        Log.Debug($"DeliverOrder 成功 account={accountId} slot={slot} energyReward={energyReward} pietyReward={pietyReward} fragItem={fragmentItemId} fragReward={fragmentReward} energyBalance={energyBalance} pietyBalance={pietyBalance} fragBalance={fragmentBalance} mask={doc.OrderDeliveredMask}");
        return (DeliverOrderResultCode.Success, energyReward, pietyReward, energyBalance, pietyBalance, fragmentItemId, fragmentReward, fragmentBalance, BuildSnapshot(doc));
    }

    /// <summary>从 DB 读最新 doc 并构造快照(失败 / 找不到玩家返回零长度 ActiveOrders 的占位 snapshot)。</summary>
    public static async FTask<MergeOrderSnapshot> TryBuildSnapshotFromDb(PlayerPropertyServiceComponent service, string accountId)
    {
        if (service?.Players == null) return MergeOrderSnapshot.Create();
        try
        {
            var doc = await service.Players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return MergeOrderSnapshot.Create();
            return BuildSnapshot(doc);
        }
        catch (MongoException e)
        {
            Log.Warning($"MergeOrderServiceHelper.TryBuildSnapshotFromDb 失败 account={accountId},err={e.Message}");
            return MergeOrderSnapshot.Create();
        }
    }
}
