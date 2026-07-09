using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using GameConfig;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 女神满档领取奖励裁决核心(服务端权威)。
///
/// 女神清屏计数 GoddessRating 走 PlayerProperty 骨架(全清 +1,封顶 = global id=7 GoddessMaxCount,
/// = service.GoddessRatingUpperBound;满档后客户端再 +1 被原子写 OverLimit 拒,停在满档等领取)。
/// 本 Helper 处理「领取」:原子 CAS 校验满档 → 清零 GoddessRating → 组装奖励 payload 回带,可循环。
///
/// 奖励语义:元素类型 = 当前未交付订单所需类型(服务端权威订单态 OrderCursor + OrderDeliveredMask 派生;
/// 无未交付订单回退当前批槽0池行类型);等级 + 数量 = Luban block.TbGoddessReward 各行(当前 Lv1×5 + Lv2×2)。
/// 元素实际入客户端合成区(局内 blob,服务端不持有)由客户端收到响应后 AddDirect 执行——服务端只裁定「能否领 + 发什么」。
///
/// 反作弊红线:
///   - 领取资格 + 清零 = 原子 CAS(filter 含 GoddessRating >= max),范式同 MergeOrderServiceHelper 的 claim-then-act:
///     并发同账号多路到达,MongoDB FindOneAndUpdate 原子保证只一路命中(before != null)、其余返 null → NotFull,
///     结构性堵死「重复领 / 未满领」,不依赖频率闸偶然兜底。
///   - 奖励内容由服务端按订单态 + 表自算,不接客户端上报。
///
/// 时钟:统一用 TimeHelper.Now(服务端 Unix 毫秒);客户端时钟篡改影响不到本侧。
/// </summary>
public static class GoddessClaimServiceHelper
{
    /// <summary>无未交付订单且订单池不可用时的回退发放元素类型(= 客户端 MergeElement.Star)。</summary>
    private const int DefaultRewardElementType = 4;

    /// <summary>
    /// 领取女神满档奖励(原子 CAS 幂等)。返回 (结果码, 发放元素类型, 奖励列表[(等级,数量)], 领取后权威计数)。
    /// 失败(未登录由 handler 前置拦 / NotFull / ServiceUnavailable)时元素类型 = 0、奖励列表为空、计数 = 0。
    /// 成功时 newRating = CAS 清零后的 GoddessRating(恒 0),供客户端对账本地计数。
    /// </summary>
    public static async FTask<(GoddessClaimResultCode code, int elementType, List<(int level, int count)> rewards, long newRating)>
        TryClaim(Scene scene, string accountId)
    {
        var empty = new List<(int, int)>();

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return (GoddessClaimResultCode.ServiceUnavailable, 0, empty, 0L);
        }

        var players = service.Players;
        var nowMs = TimeHelper.Now;

        // 读 doc:用于派生「当前未交付订单所需元素类型」(GoddessRating 满档判定交由后续 CAS 原子裁决)。
        PlayerDoc doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"GoddessClaimServiceHelper.TryClaim 读 doc 失败 account={accountId},err={e.Message}");
            return (GoddessClaimResultCode.ServiceUnavailable, 0, empty, 0L);
        }
        if (doc == null)
        {
            // 未首登(登录链路已保证;实战防御)。
            return (GoddessClaimResultCode.ServiceUnavailable, 0, empty, 0L);
        }

        // 订单懒结算(到点整批刷新),使「未交付订单」视图与客户端一致(同 TryDeliver 先结算再裁决)。
        await MergeOrderServiceHelper.ApplyOrderRefreshIfDue(service, accountId, doc, nowMs);

        // 奖励表(等级,数量):缺表 / 空表 = 部署级故障,可见降级不静默发空奖。
        var rewards = ReadRewardConfig();
        if (rewards.Count == 0)
        {
            Log.Error($"GoddessClaimServiceHelper.TryClaim 奖励表 TbGoddessReward 缺失/空,领取返 ServiceUnavailable account={accountId}。请检查导表与 GameConfigBytes。");
            return (GoddessClaimResultCode.ServiceUnavailable, 0, empty, 0L);
        }

        var elementType = ResolveRewardElementType(doc);

        // ── CAS 清零:仅当 GoddessRating >= 满档值才置 0(before != null = 本路抢占成功 = 满档且首次领)。
        // 满档值 = service.GoddessRatingUpperBound(= global id=7,与 GoddessRating 封顶同源)。
        long max = service.GoddessRatingUpperBound;
        const long clearedRating = 0L; // 领取后清零值:CAS 写入 = 回带客户端的权威 newRating,同源不漂移
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Gte(x => x.GoddessRating, max));
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.GoddessRating, clearedRating)
            .Set(x => x.LastChangeUnixMs, nowMs);

        PlayerDoc before;
        try
        {
            before = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.Before });
        }
        catch (MongoException e)
        {
            Log.Warning($"GoddessClaimServiceHelper.TryClaim 清零 CAS 失败 account={accountId},err={e.Message}");
            return (GoddessClaimResultCode.ServiceUnavailable, 0, empty, 0L);
        }

        if (before == null)
        {
            // 未满档(GoddessRating < max)或并发已被领:计数不变,不发奖。
            return (GoddessClaimResultCode.NotFull, 0, empty, 0L);
        }

        // 流水:记本次清零(BalanceBefore = 满档值,BalanceAfter = 0,delta = -满档值),口径同 ChangeProperty 旁路 append。
        long oldRating = before.GoddessRating;
        await AttrLedgerHelper.AppendAsync(
            service, accountId, PropertyType.GoddessRating,
            balanceBefore: oldRating,
            balanceAfter: 0L,
            delta: -oldRating,
            reasonRaw: "goddess-claim",
            timestampMs: nowMs);

        // delta 推送清零后的 GoddessRating = 0,供该账号其它在线会话对齐(发起会话据领取响应自行清零本地条)。
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.GoddessRating, clearedRating, "goddess-claim");

        Log.Debug($"Goddess 领取成功 account={accountId} oldRating={oldRating} elementType={elementType} rewardRows={rewards.Count}");
        return (GoddessClaimResultCode.Success, elementType, rewards, clearedRating);
    }

    /// <summary>
    /// 派生发放元素类型:遍历激活槽,取第一个未交付(mask 位未置)订单的所需元素类型;
    /// 无未交付订单(全交付,刷新未到点)→ 回退当前批槽0的池行类型;订单池不可用 → 默认 Star。
    /// </summary>
    private static int ResolveRewardElementType(PlayerDoc doc)
    {
        for (int slot = 0; slot < MergeOrderConfigServer.ActiveOrders; slot++)
        {
            if ((doc.OrderDeliveredMask & (1 << slot)) != 0) continue; // 已交付槽跳过
            var entry = MergeOrderConfigServer.GetActiveOrder(doc.OrderCursor, slot);
            if (entry != null && entry.ElementType != MergeOrderConfigServer.OrderTypeNone)
            {
                return entry.ElementType;
            }
        }

        var fallback = MergeOrderConfigServer.GetActiveOrder(doc.OrderCursor, 0);
        return fallback != null && fallback.ElementType != MergeOrderConfigServer.OrderTypeNone
            ? fallback.ElementType
            : DefaultRewardElementType;
    }

    /// <summary>读女神奖励表各行 (等级, 数量)(过滤非法行)。表缺失 / 空 → 空列表(调用方按 ServiceUnavailable 降级)。</summary>
    private static List<(int level, int count)> ReadRewardConfig()
    {
        var list = new List<(int, int)>();
        var tb = GameConfigSystem.Tables?.TbGoddessReward;
        if (tb == null) return list;
        foreach (var row in tb.DataList)
        {
            if (row.Level >= 1 && row.Count > 0)
            {
                list.Add((row.Level, row.Count));
            }
        }
        return list;
    }
}
