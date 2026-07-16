using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 排行榜结算编排核心逻辑(服务端唯一权威,设计 33)。
/// 到点判定(服务端时钟四档,§3.1)→ 读全服分数排序算各账号名次(复用设计 31 + 设计 22 §3.3.2,§3.2)
///   → 逐上榜账号名次落档查奖励库 id → 经设计 32 服务端发奖入口投结算邮件 → 原子写本榜已结标记(§3.3)。
///
/// 幂等(本刀核心):
///   - 周期幂等键 = 「本周期应结时刻」(LastSettledPeriodMs)。周循环 = 本周星期 X 时刻;一次性 = 该次时刻(结过永不再结)。
///   - 「判本周期未结 + 写本周期已结标记」用 rank_settle 的 FindOneAndUpdate(filter 存量 &lt; 本周期时刻, $set 占位, upsert) 单次原子。
///     并发两次结算检查只有一次原子成功 → 执行发奖;另一次 filter 不匹配 + 主键冲突(11000)→ 判已结、跳过(SV6/SV8 不重复发)。
///
/// 原子占位在发奖之前(claim-then-act):先抢占本周期标记、抢到才发奖。
///   理由:并发安全(SV8)的闸必须在发奖前——否则两次 tick 都先判「未结」、各自发一遍(同周期双发)。
///   代价:抢占成功后、整榜发完前进程崩溃 → 该榜本周期不再补发(部分账号漏发),靠运营补偿;
///   这是榜级幂等的窄崩溃窗(设计 §3.3 / §四 / O4),安全默认接受(崩溃概率低 + 邮件可补偿 + 不多发奖)。
///
/// 各失败/跳过分支不发奖、不抛异常致服务中断(SV11):
///   - 服务未就绪(MongoDB 不可达)→ 不结算、不写标记,下次重试(BLOCKED-环境)。
///   - 读分数/排序异常 → 本榜本次不完成、不写标记,下次节律重试(因占位在发奖前,异常发生在占位后则本周期已抢占,见 catch 处置)。
///
/// 设计基线:design-docs/33-rank-settle-server.md §三/§四/§五。
/// </summary>
public static class RankSettleHelper
{
    /// <summary>MongoDB 重复键错误码。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>结算邮件标题/正文兜底文案(邮件模板缺失时用,设计 33 §3.5)。</summary>
    private const string SettleTitleFallback = "排行榜结算奖励";
    private const string SettleContentFallback = "恭喜您在本期排行榜获得奖励，请查收。";

    // ── 结算检查入口(被进程内调度调用,§3.4) ────────────────────────

    /// <summary>
    /// 遍历全部榜,对到点且本周期未结的榜执行结算。
    /// nowMs 为服务端当前时间(生产传 TimeHelper.Now;server-test 传可控时钟驱动 SV1 各档)。
    /// 返回本次实际结算了的榜 id 列表(供日志/测试断言;未到点/已结/服务未就绪的榜不在列表)。
    /// 不抛异常:单榜失败被本榜 catch 兜住,不影响其他榜、不中断服务(SV11)。
    /// </summary>
    public static async FTask<List<int>> CheckAndSettleAll(RankServiceComponent self, long nowMs)
    {
        var settled = new List<int>();

        // 服务未就绪(MongoDB 不可达):不结算、不写标记,下次重试(设计 §四 BLOCKED-环境)。
        if (self.Scores == null || self.SettleMarks == null)
        {
            return settled;
        }

        // 遍历榜定义缓存(快照值类型枚举安全;结算只读缓存配置,分数/标记权威走 MongoDB)。
        foreach (var def in new List<RankDefDoc>(self.DefCache.Values))
        {
            try
            {
                if (await TrySettleOne(self, def, nowMs))
                {
                    settled.Add(def.RankId);
                }
            }
            catch (Exception e)
            {
                // 单榜结算异常不中断整个检查 / 不抛致服务进程崩(SV11)。本榜本次未必完成,下次节律重试。
                Log.Error($"RankSettleHelper: 榜 {def.RankId} 结算异常,跳过该榜,下次重试。{e}");
            }
        }

        return settled;
    }

    /// <summary>
    /// 对单个榜:判到点 → 抢占本周期标记(原子)→ 抢到则读分数算名次逐档发奖。
    /// 返回 true 表示本次实际结算了该榜(抢占成功并执行了发奖编排);未到点/已结/抢占失败返 false。
    /// </summary>
    private static async FTask<bool> TrySettleOne(RankServiceComponent self, RankDefDoc def, long nowMs)
    {
        // 1. 到点判定(服务端时钟四档,§3.1)。未到点 → 本周期应结时刻为 null,跳过。
        var periodMs = ComputeDuePeriodMs(def, nowMs);
        if (periodMs == null)
        {
            return false;
        }

        // 2. 抢占本周期已结标记(原子条件写,§3.3)。抢占失败 = 本周期已结(并发/重启)→ 跳过、不重复发。
        if (!await TryClaimSettlePeriod(self.SettleMarks!, def.RankId, periodMs.Value, nowMs))
        {
            return false;
        }

        // 3. 抢占成功:读全服分数排序算名次,逐上榜账号名次落档发结算邮件(§3.2)。
        await SettleRank(self, def, nowMs);
        Log.Info($"RankSettleHelper: 榜 {def.RankId} 本周期结算完成(本周期应结时刻={periodMs.Value})。");
        return true;
    }

    // ── 结算时机判定(§3.1) ──────────────────────────────────────

    /// <summary>
    /// 算本榜「本周期应结时刻」:到点返回该时刻(Unix 毫秒, UTC),未到点返回 null。
    /// 该时刻既作「是否到点」的判据,又作幂等键(§3.3:本周期应结时刻 &gt; 上次标记 → 本周期可结)。
    ///   - type=0 持续开启:永不结算 → null。
    ///   - type=1 开服 X 天:nowMs &gt;= 开服 + X 天 → 应结时刻 = 开服 + X 天;否则 null。一次性。
    ///   - type=2 指定时间(valid_val=Unix 秒):nowMs &gt;= 该时刻 → 应结时刻 = 该时刻;否则 null。一次性。
    ///   - type=3 周循环(valid_val=星期 1..7):算 nowMs 所在自然周的「星期 X 结算时刻」;nowMs &gt;= 该时刻 → 应结时刻 = 该时刻;否则 null。每周一次。
    /// 一次性(1/2)的应结时刻是固定常量(同次永远算出同值),配合 rank_settle 标记「结过即 &gt;= 标记 → 永不再结」。
    /// 周循环的应结时刻每周递增(本周时刻 &gt; 上周标记 → 下周可结)。
    /// </summary>
    private static long? ComputeDuePeriodMs(RankDefDoc def, long nowMs)
    {
        switch (def.ValidType)
        {
            case 0: // 持续开启:永不结算(只供查榜)。
                return null;

            case 1: // 开服 X 天:应结时刻 = 开服时刻 + X 天。
            {
                var dueMs = ServerOpenUnixMs + def.ValidVal * TimeHelper.OneDay;
                return nowMs >= dueMs ? dueMs : (long?)null;
            }

            case 2: // 指定时间(valid_val = Unix 秒):应结时刻 = 该秒 * 1000。
            {
                var dueMs = def.ValidVal * 1000L;
                return nowMs >= dueMs ? dueMs : (long?)null;
            }

            case 3: // 周循环:本周「星期 X 结算时刻」。
            {
                var dueMs = ComputeWeeklyDueMs(def.ValidVal, nowMs);
                return nowMs >= dueMs ? dueMs : (long?)null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// 算 nowMs 所在自然周「星期 weekday(1=周一..7=周日)结算时刻」的 Unix 毫秒(UTC)。
    /// 时刻精度到「天」(取该天 00:00 UTC,同设计 22 §3.5.1 / 设计 33 O3:小时统一,精确到小时另开)。
    /// 自然周以周一为首日。weekday 越界(&lt;1 或 &gt;7)兜底取周一(不抛)。
    /// </summary>
    private static long ComputeWeeklyDueMs(long weekday, long nowMs)
    {
        if (weekday < 1)
        {
            weekday = 1;
        }
        else if (weekday > 7)
        {
            weekday = 7;
        }

        var now = nowMs.Transition(); // Unix 毫秒 → UTC DateTime。
        var nowDate = now.Date;       // 当天 00:00 UTC。
        // .NET DayOfWeek:Sunday=0..Saturday=6;转成 1=周一..7=周日。
        var isoNowWeekday = ((int)nowDate.DayOfWeek + 6) % 7 + 1;
        // 本周一 00:00 = 当天 00:00 - (今天是本周第几天 - 1) 天。
        var mondayDate = nowDate.AddDays(-(isoNowWeekday - 1));
        var dueDate = mondayDate.AddDays(weekday - 1); // 本周星期 weekday 00:00 UTC。
        return dueDate.Transition();
    }

    // ── 结算幂等(§3.3) ─────────────────────────────────────────

    /// <summary>
    /// 抢占本周期结算标记:仅当存量标记 &lt; 本周期应结时刻才占位成功(原子条件写)。
    ///   - 标记文档不存在 → upsert 按 _id=rankId 建文档(首次结算)→ 抢占成功。
    ///   - 存量 LastSettledPeriodMs &lt; periodMs → 原子更新为 periodMs → 抢占成功(周循环下周到点)。
    ///   - 存量 LastSettledPeriodMs &gt;= periodMs → filter 不匹配 + upsert 撞 _id 主键(11000)→ 抢占失败(本周期已结)。
    /// 并发两次只有一次抢占成功,另一次失败跳过(SV6/SV8);持久 MongoDB 跨会话/重启(SV7/SV10)。
    /// 返回 true = 抢到本周期(可发奖);false = 已被占(已结,跳过)。
    /// </summary>
    private static async FTask<bool> TryClaimSettlePeriod(
        IMongoCollection<RankSettleMarkDoc> marks, int rankId, long periodMs, long nowMs)
    {
        var filter = Builders<RankSettleMarkDoc>.Filter.And(
            Builders<RankSettleMarkDoc>.Filter.Eq(x => x.RankId, rankId),
            Builders<RankSettleMarkDoc>.Filter.Lt(x => x.LastSettledPeriodMs, periodMs));
        var update = Builders<RankSettleMarkDoc>.Update
            .Set(x => x.LastSettledPeriodMs, periodMs)
            .Set(x => x.SettledAtMs, nowMs);
        var options = new FindOneAndUpdateOptions<RankSettleMarkDoc> { IsUpsert = true };

        try
        {
            await marks.FindOneAndUpdateAsync(filter, update, options);
            return true;
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return false;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    // ── 结算编排:读分数算名次 → 逐档发奖(§3.2) ──────────────────

    /// <summary>
    /// 抢占成功后的实际结算:读全服分数(复用设计 31 存储 + 索引)→ 排序算各账号名次 → 逐上榜账号名次落档发结算邮件。
    /// 排序口径与设计 31 查榜一致:分数降序 + 同分达到时间升序 + 顺序名次(同分各占唯一名次, 设计 22 §3.3.2)。
    /// 过滤入榜要求(BestScore &lt; EnterCondition 不参与)、截入榜上限(RankCountMax 外不参与名次)。
    /// 各上榜账号:名次落档(无档/档奖=0 → 不发该账号)、有档 → 经设计 32 SendMailTo 投结算邮件(挂名次档 reward 库 id)。
    /// </summary>
    private static async FTask SettleRank(RankServiceComponent self, RankDefDoc def, long nowMs)
    {
        var scores = self.Scores!;

        // 取该榜达入榜要求的分数,按 (分数降序 + 同分达到时间升序) 排序;入榜上限内取前 N(贴合设计 31 复合索引)。
        var filter = Builders<RankScoreDoc>.Filter.And(
            Builders<RankScoreDoc>.Filter.Eq(x => x.RankId, def.RankId),
            Builders<RankScoreDoc>.Filter.Gte(x => x.BestScore, def.EnterCondition));
        var sort = Builders<RankScoreDoc>.Sort
            .Descending(x => x.BestScore)
            .Ascending(x => x.AchievedUnixMs);
        var find = scores.Find(filter).Sort(sort);
        if (def.RankCountMax > 0)
        {
            find = find.Limit(def.RankCountMax);
        }
        var ranked = await find.ToListAsync();

        // 取邮件组件(同 Gate Scene 上,设计 32 服务端发奖入口的载体)。未就绪 → 无法发奖,记日志、不抛(本周期已抢占,不补发,§四)。
        var mailComponent = self.Scene.GetComponent<MailServiceComponent>();
        if (mailComponent == null || mailComponent.Directed == null)
        {
            Log.Warning($"RankSettleHelper: 榜 {def.RankId} 结算时邮件服务未就绪,本周期上榜账号未发结算邮件(已抢占标记,不补发)。");
            return;
        }

        // 取结算邮件模板(标题/正文/有效期)。mail def id=0 → 整榜不发(§3.2 边界);非 0 但模板缺失 → 兜底占位(§3.5)。
        var (title, content, expireDays) = ResolveSettleMail(mailComponent, def.MailDefId);
        if (def.MailDefId == 0)
        {
            // 榜无结算邮件模板:整榜不发,仅已抢占标记(§3.2 ②)。
            Log.Info($"RankSettleHelper: 榜 {def.RankId} 无结算邮件模板(mail=0),不发结算邮件,仅记已结。");
            return;
        }

        var sentCount = 0;
        for (var i = 0; i < ranked.Count; i++)
        {
            var rank = i + 1; // 顺序名次(1 起,同分各占唯一名次)。
            var rewards = TierRewardForRank(def, rank);
            if (rewards.Count == 0)
            {
                // 名次未落任何档 / 档奖为空 → 该账号不发(§3.2 边界),不影响其他账号。
                continue;
            }

            // 经设计 32 服务端发奖入口投结算邮件:标题/正文/有效期取邮件模板(缺失则兜底占位, ResolveSettleMail 已处理),
            // 发件人统一用结算占位(§3.5),附件用名次档内联奖励条目(§3.2 旁注 / SV5)。
            var mailId = await MailDecisionHelper.SendMailTo(
                mailComponent, ranked[i].Account, MailSenderType.RankReward, title, content, expireDays, rewards);
            if (mailId != null)
            {
                sentCount++;
            }
            else
            {
                // 单账号投递失败不让整榜结算崩(SV11);本周期已抢占,该账号本周期不补发(§四 取舍)。
                Log.Warning($"RankSettleHelper: 榜 {def.RankId} 名次 {rank} 账号 {ranked[i].Account} 结算邮件投递失败(邮件服务不可用),跳过该账号。");
            }
        }

        Log.Info($"RankSettleHelper: 榜 {def.RankId} 结算发奖完成,上榜参与名次 {ranked.Count} 人,投出结算邮件 {sentCount} 封。");
    }

    /// <summary>
    /// 按名次查中奖档的内联奖励条目;名次未落任何档返回空列表(= 不发)。
    /// 名次档已按 RankMin 升序聚合(榜定义播种时排序),逐档判区间命中(rank ∈ [RankMin, RankMax])。
    /// </summary>
    private static List<RewardEntryDoc> TierRewardForRank(RankDefDoc def, int rank)
    {
        if (def.Tiers == null)
        {
            return new List<RewardEntryDoc>();
        }
        foreach (var tier in def.Tiers)
        {
            if (rank >= tier.RankMin && rank <= tier.RankMax)
            {
                return tier.Rewards;
            }
        }
        return new List<RewardEntryDoc>(); // 名次落档区间空隙(如档只覆盖 1/2-10/11-100,名次 101 超出)→ 无奖(§3.2 边界)。
    }

    /// <summary>
    /// 取结算邮件模板的(标题 textId, 正文 textId, 有效期天数)。
    /// mail def id 在运营模板缓存(键为模板 id 字符串)命中 → 用模板的标题/正文/有效期;
    /// 不命中(模板缺失)→ 用兜底占位标题(§3.5),有效期取 0(由邮件入口按全局兜底天数处理)。
    /// </summary>
    private static (string title, string content, int expireDays) ResolveSettleMail(
        MailServiceComponent mailComponent, int mailDefId)
    {
        if (mailDefId != 0 && mailComponent.TemplateCache.TryGetValue(mailDefId.ToString(), out var template))
        {
            return (template.Title, template.Content, template.ExpireDays);
        }
        return (SettleTitleFallback, SettleContentFallback, 0);
    }

    // ── 开服基准时刻(type=1 开服 X 天用) ─────────────────────────

    /// <summary>
    /// 服务端开服基准时刻(Unix 毫秒, UTC),供「开服 X 天」结算判定(type=1)。
    /// 进程启动时由 RankServiceComponentSystem 初始化为进程基准(设计 33 §3.1「开服日期取服务端配置 / 进程基准」)。
    /// server-test 可通过该入口设可控基准驱动 SV1 type=1 各分支。
    /// </summary>
    public static long ServerOpenUnixMs { get; set; } = TimeHelper.Now;
}
