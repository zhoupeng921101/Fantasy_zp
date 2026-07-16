using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 排行榜每日/点赞奖领取的服务端权威裁决(每日/点赞奖迁服务端权威,替代客户端本地自发奖 + 本地存领取态)。
/// 与结算奖(RankSettleHelper 服务端周期结算、榜级)不同,每日/点赞由玩家主动领、每玩家每类每日一次:
///   判资格(每日=在榜 + 名次档有每日奖;点赞=榜级有点赞奖,不要求在榜)→ 原子占位今日已领标记(防同日重领)→ 经 SendMailTo 投奖励邮件(邮件领取时到账)。
/// 幂等(防重领,claim-then-act):先原子抢占今日标记(rank_claim,filter 存量 &lt; 今日 00:00 + upsert),抢到才发奖;
///   同日再领 filter 不匹配 + upsert 撞 _id(11000)→ AlreadyClaimedToday。抢占后、发邮件前进程崩 → 该玩家该日漏发(窄崩溃窗,同结算取舍,ops 可补)。
/// server-perf:领取低频(玩家主动、每类每日一次),每次一读(每日判名次)+ 一次原子标记写 + 一次邮件插入,量级可接受;可领态查询复用查榜已算 myRank、只多一次标记读。
/// </summary>
public static class RankClaimHelper
{
    /// <summary>领取类型:每日奖(按名次档,须在榜)。</summary>
    public const int ClaimTypeDaily = 1;
    /// <summary>领取类型:点赞奖(榜级不分档,不要求在榜)。</summary>
    public const int ClaimTypePraise = 2;

    /// <summary>MongoDB 重复键错误码(同 RankSettleHelper)。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>每日奖邮件文案。</summary>
    private const string DailyTitle = "每日排行奖励";
    private const string DailyContent = "恭喜获得每日排行奖励，请查收。";
    /// <summary>点赞奖邮件文案。</summary>
    private const string PraiseTitle = "点赞奖励";
    private const string PraiseContent = "感谢您的点赞支持，奖励请查收。";

    /// <summary>
    /// 领取一个榜的每日/点赞奖。account 为服务端从会话取的账号(非客户端自报)。
    /// 返回裁决结果码;成功时已投奖励邮件到收件箱 + 写今日已领标记(奖励在邮件领取时到账)。
    /// </summary>
    public static async FTask<RankClaimResultCode> Claim(
        RankServiceComponent service, string account, int rankId, int claimType, long nowMs)
    {
        if (service.ClaimMarks is not { } claimMarks || service.Scores == null)
        {
            return RankClaimResultCode.ServiceUnavailable; // MongoDB 未就绪
        }
        if (!service.DefCache.TryGetValue(rankId, out var def))
        {
            return RankClaimResultCode.NoReward; // 榜 id 查不到
        }

        // 1. 判资格 + 取该类奖励条目。
        List<RewardEntryDoc> rewards;
        string title;
        string content;
        if (claimType == ClaimTypeDaily)
        {
            // 每日奖:须在榜(名次 > 0),按名次落档取每日奖。
            var (queryCode, _, myRank, _) = await RankDecisionHelper.Query(service, account, rankId);
            if (queryCode != RankQueryResultCode.Success)
            {
                return RankClaimResultCode.ServiceUnavailable;
            }
            if (myRank == 0)
            {
                return RankClaimResultCode.NotRanked;
            }
            rewards = DailyRewardForRank(def, myRank);
            title = DailyTitle;
            content = DailyContent;
        }
        else if (claimType == ClaimTypePraise)
        {
            // 点赞奖:榜级不分档,不要求在榜。
            rewards = def.PraiseRewards ?? new List<RewardEntryDoc>();
            title = PraiseTitle;
            content = PraiseContent;
        }
        else
        {
            return RankClaimResultCode.NoReward; // ClaimType 非法
        }

        if (rewards.Count == 0)
        {
            return RankClaimResultCode.NoReward; // 该名次档无每日奖 / 该榜无点赞奖
        }

        // 2. 取邮件组件(发奖载体);未就绪 → 服务不可用(不占标记,可重试)。
        var mailComponent = service.Scene.GetComponent<MailServiceComponent>();
        if (mailComponent == null || mailComponent.Directed == null)
        {
            return RankClaimResultCode.ServiceUnavailable;
        }

        // 3. 原子抢占今日已领标记(claim-then-act):今日 00:00 > 存量标记才占位成功,防同日重领。
        var todayMs = DayStartMs(nowMs);
        var (ok, alreadyToday) = await TryClaimToday(claimMarks, account, rankId, claimType, todayMs);
        if (!ok)
        {
            return alreadyToday ? RankClaimResultCode.AlreadyClaimedToday : RankClaimResultCode.ServiceUnavailable;
        }

        // 4. 抢到今日标记 → 经服务端发奖入口投奖励邮件(领取时全部到账)。
        //    发信失败(MongoDB 抖动)→ 标记已占、本日不补发(窄崩溃窗,同结算取舍);记 Warning,仍返成功(标记已记)。
        var mailId = await MailDecisionHelper.SendMailTo(
            mailComponent, account, MailSenderType.RankReward, title, content, 0, rewards);
        if (mailId == null)
        {
            Log.Warning($"RankClaimHelper: 榜 {rankId} 账号 {account} 领取 type={claimType} 已占今日标记但奖励邮件投递失败(邮件服务抖动),本日不补发。");
        }
        return RankClaimResultCode.Success;
    }

    /// <summary>
    /// 算该榜今日「每日奖 / 点赞奖是否可领」(查榜 handler 复用已算好的 myRank,避免重排)。
    /// 每日:名次 > 0 + 名次档有每日奖 + 今日未领;点赞:该榜有点赞奖 + 今日未领。
    /// 读标记文档失败 / MongoDB 未就绪 → 均返 false(不误报可领)。
    /// </summary>
    public static async FTask<(bool daily, bool praise)> ComputeClaimable(
        RankServiceComponent service, string account, int rankId, int myRank, long nowMs)
    {
        if (service.ClaimMarks is not { } claimMarks || !service.DefCache.TryGetValue(rankId, out var def))
        {
            return (false, false);
        }

        bool dailyHasReward = myRank > 0 && DailyRewardForRank(def, myRank).Count > 0;
        bool praiseHasReward = (def.PraiseRewards?.Count ?? 0) > 0;
        if (!dailyHasReward && !praiseHasReward)
        {
            return (false, false);
        }

        RankClaimMarkDoc mark;
        try
        {
            mark = await claimMarks
                .Find(Builders<RankClaimMarkDoc>.Filter.Eq(x => x.UniqueKey, ClaimKey(account, rankId)))
                .FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"RankClaimHelper.ComputeClaimable 读标记失败 account={account} rankId={rankId},err={e.Message}");
            return (false, false);
        }

        var todayMs = DayStartMs(nowMs);
        long lastDaily = mark?.LastDailyClaimDayMs ?? 0;
        long lastPraise = mark?.LastPraiseClaimDayMs ?? 0;
        return (dailyHasReward && todayMs > lastDaily, praiseHasReward && todayMs > lastPraise);
    }

    // ── 内部 ──────────────────────────────────────────────

    /// <summary>
    /// 原子抢占今日已领标记:今日 00:00 &gt; 存量该类标记才占位成功。
    /// 返回 (ok=是否抢到, alreadyToday=是否因今日已领而失败);其他 MongoDB 错 → (false, false) 服务不可用。
    /// </summary>
    private static async FTask<(bool ok, bool alreadyToday)> TryClaimToday(
        IMongoCollection<RankClaimMarkDoc> claimMarks, string account, int rankId, int claimType, long todayMs)
    {
        var key = ClaimKey(account, rankId);
        Expression<Func<RankClaimMarkDoc, long>> dayField;
        if (claimType == ClaimTypeDaily)
        {
            dayField = x => x.LastDailyClaimDayMs;
        }
        else
        {
            dayField = x => x.LastPraiseClaimDayMs;
        }

        // filter:_id 匹配 且 该类今日未领(存量 < 今日 00:00)。upsert 时按 _id=key 建文档(首次领)。
        var filter = Builders<RankClaimMarkDoc>.Filter.And(
            Builders<RankClaimMarkDoc>.Filter.Eq(x => x.UniqueKey, key),
            Builders<RankClaimMarkDoc>.Filter.Lt(dayField, todayMs));
        var update = Builders<RankClaimMarkDoc>.Update
            .Set(dayField, todayMs)
            .SetOnInsert(x => x.Account, account)
            .SetOnInsert(x => x.RankId, rankId);
        var options = new FindOneAndUpdateOptions<RankClaimMarkDoc> { IsUpsert = true };

        try
        {
            await claimMarks.FindOneAndUpdateAsync(filter, update, options);
            return (true, false);
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return (false, true); // _id 存在但该类今日已领 → filter 不匹配 + upsert 撞主键
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return (false, true);
        }
        catch (MongoException e)
        {
            Log.Warning($"RankClaimHelper.TryClaimToday 写标记失败 account={account} rankId={rankId} type={claimType},err={e.Message}");
            return (false, false); // 其他 MongoDB 错 → 服务不可用(未占标记)
        }
    }

    /// <summary>按名次查中档的每日奖内联条目;名次未落任何档 / 该档无每日奖 → 空列表。</summary>
    private static List<RewardEntryDoc> DailyRewardForRank(RankDefDoc def, int rank)
    {
        if (def.Tiers == null)
        {
            return new List<RewardEntryDoc>();
        }
        foreach (var tier in def.Tiers)
        {
            if (rank >= tier.RankMin && rank <= tier.RankMax)
            {
                return tier.DailyRewards ?? new List<RewardEntryDoc>();
            }
        }
        return new List<RewardEntryDoc>();
    }

    /// <summary>rank_claim 复合 _id:"{account}|{rankId}"(同 rank_score 键约定)。</summary>
    private static string ClaimKey(string account, int rankId) => account + "|" + rankId;

    /// <summary>Unix 毫秒 → 当天 00:00 UTC 的 Unix 毫秒(跨天判定基准,同结算周时刻按天取整)。</summary>
    private static long DayStartMs(long nowMs) => nowMs.Transition().Date.Transition();
}
