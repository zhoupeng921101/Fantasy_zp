using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 塔罗牌购买进度裁决核心(塔罗收集系统,与既有 TarotBlindBox 盲盒是两个系统)。
///
/// 配置源:Luban block.TbTarotCard(牌 → 每进度虔诚币成本数组 UnlockCosts;数组长度 = 总进度步数);
/// 持久态:PlayerDoc.Piety(虔诚币余额)+ PlayerDoc.TarotProgress(cardId → 已购步数)。
///
/// 反作弊红线:
///   - 每步成本 = 服务端按 UnlockCosts[当前步] 自算,**不**接受客户端上报;
///   - 「虔诚币足额 + 该牌步数匹配」校验与「扣币 + 进度 +1」在**同一条**原子 FindOneAndUpdate 内
///     (filter: Piety ≥ cost 且 TarotProgress.<card> == 当前步;update: $inc Piety 负扣 + $inc 进度 +1):
///     并发同账号同牌多路请求,MongoDB 原子保证只一路命中该步,结构性堵死「一份币买两步 / 重复买同一步」;
///   - CAS 未命中 → 重读文档区分 AlreadyMaxed / NotEnoughPiety,不发生任何写。
///
/// 收集态派生:步数 == UnlockCosts.Length 即该牌已激活,不另存「已收集集合」(避免冗余可推导数据)。
/// 旧档兼容:TarotProgress.<card> 字段 absent 时按 0 步处理(购首步的 filter 用 Or(Eq(0), Exists(false)) 覆盖 absent)。
/// </summary>
public static class TarotCollectionServiceHelper
{
    /// <summary>
    /// 购买一步进度裁决。返回 (resultCode, steps, pietyBalance, progress):
    ///   - steps = 裁决后该牌已购步数;-1 = 哨兵(未取到权威值,客户端不据此 set);
    ///   - pietyBalance = 扣减后虔诚币权威余额;-1 = 哨兵(未取到权威值);
    ///   - progress = 全牌进度全集(cardId → 步数,仅在真取自玩家文档时非 null:成功/失败回当前值供对齐);
    ///     null = 未取到(未读库的降级路径),handler 据此置 ProgressValid=false,客户端保留投影不清空。
    /// </summary>
    public static async FTask<(TarotPurchaseResultCode resultCode, int steps, long pietyBalance, Dictionary<int, int>? progress)>
        TryPurchaseStep(Scene scene, string accountId, int cardId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return (TarotPurchaseResultCode.ServiceUnavailable, -1, -1L, null);
        }

        var card = GameConfigSystem.Tables?.TbTarotCard?.GetOrDefault(cardId);
        if (card == null)
        {
            return (TarotPurchaseResultCode.UnknownCard, -1, -1L, null);
        }
        var costs = card.UnlockCosts;
        if (costs == null || costs.Count == 0)
        {
            // 表行配置非法(成本数组缺失):按服务不可用降级,不让非法配置变成免费/异常购买。
            Log.Error($"TarotPurchase 配置非法 card={cardId} unlockCosts=null/empty");
            return (TarotPurchaseResultCode.ServiceUnavailable, -1, -1L, null);
        }

        var players = service.Players;
        var progressField = "TarotProgress." + cardId;
        var nowMs = TimeHelper.Now;

        // ── 先读当前进度:算本步成本 + 期望步数(CAS 的 filter 依赖) ──────────────
        PlayerDoc? cur;
        try
        {
            cur = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotPurchase 读 doc 失败 account={accountId} card={cardId},err={e.Message}");
            return (TarotPurchaseResultCode.ServiceUnavailable, -1, -1L, null);
        }
        if (cur == null)
        {
            // 未首登(理论上登录链路已保证;实战防御)。
            return (TarotPurchaseResultCode.ServiceUnavailable, -1, -1L, null);
        }

        int curSteps = ReadProgress(cur, cardId);
        if (curSteps >= costs.Count)
        {
            // 已满(已激活):幂等拒绝,不扣币。回带当前权威态供对齐。
            return (TarotPurchaseResultCode.AlreadyMaxed, curSteps, cur.Piety, BuildProgressMap(cur));
        }
        long cost = costs[curSteps];
        if (cost < 0L) cost = 0L; // sanity:负成本按 0 处理(免费步),不因配置笔误变成给玩家加币

        // ── 单条原子 CAS:虔诚币足额 + 该牌步数 == 期望 才 扣币 + 进度 +1 ──────────────
        // absent(curSteps==0)时 Eq(field,0) 不命中 absent 字段 → 用 Or(Eq(0), Exists(false)) 覆盖「未购过」形态。
        var stepMatch = curSteps == 0
            ? Builders<PlayerDoc>.Filter.Or(
                Builders<PlayerDoc>.Filter.Eq(progressField, 0),
                Builders<PlayerDoc>.Filter.Exists(progressField, false))
            : Builders<PlayerDoc>.Filter.Eq(progressField, curSteps);
        var casFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Gte(x => x.Piety, cost),
            stepMatch);
        var casUpdate = Builders<PlayerDoc>.Update
            .Inc(x => x.Piety, -cost)
            .Inc(progressField, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);

        PlayerDoc? doc;
        try
        {
            doc = await players.FindOneAndUpdateAsync(casFilter, casUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotPurchase CAS 写库失败 account={accountId} card={cardId},err={e.Message}");
            return (TarotPurchaseResultCode.ServiceUnavailable, curSteps, -1L, null);
        }

        if (doc != null)
        {
            // 购买成功:币已扣、进度 +1。记虔诚币流水(负 delta = 消耗),回权威余额 + 新步数 + 进度全集。
            int newSteps = ReadProgress(doc, cardId);
            long balance = doc.Piety;
            await AttrLedgerHelper.AppendAsync(
                service, accountId, PropertyType.Piety,
                balanceBefore: balance + cost, balanceAfter: balance, delta: -cost,
                reasonRaw: $"tarot_purchase:card{cardId}:step{newSteps}", timestampMs: nowMs);
            Log.Debug($"TarotPurchase 成功 account={accountId} card={cardId} step={newSteps}/{costs.Count} cost={cost} piety={balance}");
            return (TarotPurchaseResultCode.Success, newSteps, balance, BuildProgressMap(doc));
        }

        // ── CAS 未命中:重读区分失败原因(不发生任何写)──────────────
        PlayerDoc? fresh;
        try
        {
            fresh = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotPurchase 重读 doc 失败 account={accountId} card={cardId},err={e.Message}");
            return (TarotPurchaseResultCode.ServiceUnavailable, curSteps, -1L, null);
        }
        if (fresh == null)
        {
            return (TarotPurchaseResultCode.ServiceUnavailable, -1, -1L, null);
        }

        int freshSteps = ReadProgress(fresh, cardId);
        var freshMap = BuildProgressMap(fresh);
        if (freshSteps >= costs.Count)
        {
            return (TarotPurchaseResultCode.AlreadyMaxed, freshSteps, fresh.Piety, freshMap);
        }
        long freshCost = costs[freshSteps] < 0L ? 0L : costs[freshSteps];
        if (fresh.Piety < freshCost)
        {
            return (TarotPurchaseResultCode.NotEnoughPiety, freshSteps, fresh.Piety, freshMap);
        }
        // 足额且未满却 CAS 未命中:并发窗口内步数又变了(极罕见),按服务不可用让客户端重试。
        Log.Warning($"TarotPurchase CAS 未命中但重读足额未满(并发窗口) account={accountId} card={cardId} steps={freshSteps} piety={fresh.Piety}");
        return (TarotPurchaseResultCode.ServiceUnavailable, freshSteps, fresh.Piety, freshMap);
    }

    /// <summary>
    /// 进主界面免费推进裁决(不走虔诚币):按配置顺位(TbTarotCard.DataList,与客户端当前牌口径一致)
    /// 找第一张未集齐(已购步数 &lt; UnlockCosts.Count)的牌,原子推进一步(不扣币),回带推进后的权威进度全集。
    ///
    /// 服务端裁定当前牌与步数,不接受客户端上报;推进用单条 CAS(filter: 该牌步数 == 期望;update: $inc 进度 +1),
    /// 并发同账号多路只一路命中,结构性堵死重复推进。全部集齐 → 不写,回当前进度全集。
    /// 读/写库失败 → null(handler 据此回退保留既有投影,不推进)。
    /// </summary>
    public static async FTask<Dictionary<int, int>?> AutoAdvanceFreeStep(Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return null;
        }

        var players = service.Players;
        var nowMs = TimeHelper.Now;

        PlayerDoc? cur;
        try
        {
            cur = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotAutoAdvance 读 doc 失败 account={accountId},err={e.Message}");
            return null;
        }
        if (cur == null)
        {
            return null;
        }

        // ── 按配置顺位找第一张未集齐的牌 ──────────────
        var list = GameConfigSystem.Tables?.TbTarotCard?.DataList;
        if (list == null || list.Count == 0)
        {
            return BuildProgressMap(cur); // 无配置:不推进,回当前权威态
        }

        int targetId = -1;
        int targetMax = 0;
        int curSteps = 0;
        foreach (var card in list)
        {
            var costs = card.UnlockCosts;
            int max = costs?.Count ?? 0;
            if (max <= 0) continue; // 无成本配置的牌无法判定集齐/推进,跳过
            int steps = ReadProgress(cur, card.Id);
            if (steps < max)
            {
                targetId = card.Id;
                targetMax = max;
                curSteps = steps;
                break;
            }
        }
        if (targetId < 0)
        {
            return BuildProgressMap(cur); // 全部集齐:不写,回当前权威态
        }

        // ── 单条原子 CAS:该牌步数 == 期望 才 进度 +1(免费,不涉虔诚币)──────────────
        // absent(curSteps==0)用 Or(Eq(0), Exists(false)) 覆盖「未购过」形态(同 TryPurchaseStep 口径)。
        var progressField = "TarotProgress." + targetId;
        var stepMatch = curSteps == 0
            ? Builders<PlayerDoc>.Filter.Or(
                Builders<PlayerDoc>.Filter.Eq(progressField, 0),
                Builders<PlayerDoc>.Filter.Exists(progressField, false))
            : Builders<PlayerDoc>.Filter.Eq(progressField, curSteps);
        var casFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            stepMatch);
        var casUpdate = Builders<PlayerDoc>.Update
            .Inc(progressField, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);

        PlayerDoc? doc;
        try
        {
            doc = await players.FindOneAndUpdateAsync(casFilter, casUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotAutoAdvance CAS 写库失败 account={accountId} card={targetId},err={e.Message}");
            return null;
        }

        if (doc != null)
        {
            int newSteps = ReadProgress(doc, targetId);
            Log.Debug($"TarotAutoAdvance 推进 account={accountId} card={targetId} step={newSteps}/{targetMax}");
            return BuildProgressMap(doc);
        }

        // CAS 未命中(并发窗口内步数已变):不重试,重读回当前权威全集(下次进入再推进)。
        try
        {
            var fresh = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            return fresh != null ? BuildProgressMap(fresh) : null;
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotAutoAdvance 重读 doc 失败 account={accountId} card={targetId},err={e.Message}");
            return null;
        }
    }

    /// <summary>读某牌当前已购步数(字段 absent → 0)。</summary>
    private static int ReadProgress(PlayerDoc doc, int cardId)
    {
        if (doc.TarotProgress != null && doc.TarotProgress.TryGetValue(cardId.ToString(), out var steps))
        {
            return steps;
        }
        return 0;
    }

    /// <summary>把玩家文档的塔罗进度字典整份转成 cardId(int) → 步数(只含合法键值),供响应回带整份覆盖客户端投影。</summary>
    private static Dictionary<int, int> BuildProgressMap(PlayerDoc doc)
    {
        var map = new Dictionary<int, int>();
        if (doc.TarotProgress != null)
        {
            foreach (var kv in doc.TarotProgress)
            {
                if (int.TryParse(kv.Key, out var id) && id > 0 && kv.Value > 0)
                {
                    map[id] = kv.Value;
                }
            }
        }
        return map;
    }
}
