using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 活动达标判定 + 周期幂等抢占 + 发奖编排核心逻辑(服务端唯一权威)。
/// 设计 39 §3.3 周期键 + §3.4 发奖编排;同 33 RankSettleHelper 的 claim-then-act 范式。
///
/// 幂等(本子单核心):
///   - 周期幂等键 = 本周期键(Daily=今日 0:00 UTC ms / Weekly=本周一 0:00 UTC ms / OneShot=1 常量)。
///   - 「判本周期未发 + 写本周期已发」用 activity_progress 的 FindOneAndUpdate
///     (filter _id 匹配 且 LastClaimedCycleKey &lt; 本周期键, $set 本周期键, IsUpsert) 单次原子。
///   - 并发两次抢占只一次原子成功 → 执行发邮件;另一次 filter 不匹配 + 主键冲突(11000)→ 判已发跳过(SV6)。
///
/// 抢占在副作用(发邮件)之前(claim-then-act):
///   崩在抢占后发邮件前 → 周期键已记、邮件未投 = 漏发窄窗(运营可补);
///   反过来「先发邮件再写键」则崩在中间 → 邮件已投但键未写,重起会重发同周期奖(超发不可补)。
///   设计 §四接受漏发窄窗 + 不接受超发窄窗(同 33 §四诚实取舍)。
///
/// 时钟入参:
///   helper 接受 nowMs 入参(生产传 TimeHelper.Now;server-test 传可控时刻驱动跨日 / 跨周分支)。
///   helper 内部不读 TimeHelper.Now / DateTime.UtcNow,避免「一读即不可测」(server-dev memory `cycle-trigger-idempotency-pattern`)。
///
/// 范围:本子单仅实现 Type=Login 触发(OnLogin 入口);Cumulative/Schedule/Action 留 O3 不接,但 Increment + EvaluateAndClaim
///   接缝已存在 — 后续业务系统接入触发钩子时直接调相同 API,不动 helper 形态。
///
/// 设计基线:design-docs/39-activity-server.md §3.3 / §3.4 / §3.5。
/// </summary>
public static class ActivityEvalHelper
{
    /// <summary>MongoDB 重复键错误码(沿 32/33 同口径)。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>Type=Login(玩家登录时 +1)。同 ActivityDefDoc.Type 编号。</summary>
    public const int TypeLogin = 1;

    /// <summary>Cycle=Daily(服务端跨日 0:00 UTC 重置)。同 ActivityDefDoc.Cycle 编号。</summary>
    public const int CycleDaily = 1;

    /// <summary>Cycle=Weekly(服务端跨周一 0:00 UTC 重置)。</summary>
    public const int CycleWeekly = 2;

    /// <summary>Cycle=OneShot(永发一次性,周期键固定 = 1)。</summary>
    public const int CycleOneShot = 3;

    // ── 登录触发入口(本子单实做) ────────────────────────────────

    /// <summary>
    /// 登录时遍历所有 Type=Login 活动,各自走「Increment(counter+1) → EvaluateAndClaim(判达标 + 抢占 + 发邮件)」流程。
    /// account 为服务端从会话取得的设备账号(沿 35 accounts._id 同源);
    /// nowMs 为服务端权威时钟(生产传 TimeHelper.Now,server-test 可驱动跨日/跨周分支)。
    ///
    /// 服务未就绪(MongoDB 不可达 / 组件未挂载)→ 静默跳过、不抛、不影响登录链路(沿 35 / 32 / 37 同样的 BLOCKED-env 处置)。
    /// 单活动处理异常被本活动 catch 兜住、不中断其他活动(SV11)。
    /// 不返回值:本子单登录链路不感知活动结果(玩家可观测路径 = 邮箱拉邮件,设计 39 §3.6)。
    /// </summary>
    public static async FTask OnLogin(ActivityServiceComponent? self, MailServiceComponent? mail, string account, long nowMs)
    {
        if (self == null || self.Progress == null || self.DefCache.Count == 0)
        {
            return; // 服务未就绪 / 无活动配置 → 静默跳过(沿 35 / 32 / 37 不可用静默基线)。
        }
        if (string.IsNullOrEmpty(account))
        {
            return;
        }

        // 遍历 DefCache 快照(值类型枚举安全);本子单仅 1 套 Login 活动 = 每日登录奖。
        foreach (var def in new List<ActivityDefDoc>(self.DefCache.Values))
        {
            if (def.Type != TypeLogin)
            {
                continue;
            }

            // 开放窗口判定(StartAtMs / EndAtMs):未开 / 已关 → 跳过该活动。
            if (def.StartAtMs > 0 && nowMs < def.StartAtMs)
            {
                continue;
            }
            if (def.EndAtMs > 0 && nowMs >= def.EndAtMs)
            {
                continue;
            }

            try
            {
                // 1. counter+1(每次登录都计;Daily/Weekly 跨周期清零另起,见旁注)。
                await Increment(self, account, def.ActivityId, 1, nowMs);
                // 2. 判达标 + 抢占周期键 + 发邮件。
                await EvaluateAndClaim(self, mail, account, def, nowMs);
            }
            catch (Exception e)
            {
                // 单活动异常不中断其他活动 / 不抛致登录链路中断(SV11)。
                Log.Error($"ActivityEvalHelper.OnLogin: account={account} activityId={def.ActivityId} 处理异常,跳过。{e}");
            }
        }
    }

    // ── 计数器 +1 / +delta(进程内 API) ──────────────────────────

    /// <summary>
    /// 给 (account, activityId) 的进度计数器 +delta(Login 类传 1;Cumulative 类业务系统接入时按业务定 delta)。
    /// 原子 upsert:filter _id 匹配,$inc(Counter, delta) + $set(LastUpdatedAt) + $setOnInsert(Account/ActivityId/Version)。
    ///   - 首次该 (account, activityId) → upsert 建文档,Counter=delta、LastClaimedCycleKey=0(待抢占)、Version=1。
    ///   - 已存在 → $inc 累加 Counter,不动 LastClaimedCycleKey(由 EvaluateAndClaim 单独抢占)。
    /// 注:Counter 字段不做跨周期清零(本子单 Login + Daily 类只需 Counter ≥ Target=1 即可;周期键已经守了跨周期幂等,
    ///   Counter 跨周期保留为「累计登录次数」不影响裁决,且能为未来「累计登录 N 天」类 Cumulative 活动复用本字段语义)。
    /// </summary>
    public static async FTask Increment(ActivityServiceComponent self, string account, int activityId, long delta, long nowMs)
    {
        if (self.Progress == null)
        {
            return;
        }

        var uniqueKey = BuildKey(account, activityId);
        var filter = Builders<ActivityProgressDoc>.Filter.Eq(x => x.UniqueKey, uniqueKey);
        var update = Builders<ActivityProgressDoc>.Update
            .SetOnInsert(x => x.Account, account)
            .SetOnInsert(x => x.ActivityId, activityId)
            .SetOnInsert(x => x.LastClaimedCycleKey, 0L)
            .SetOnInsert(x => x.Version, 1)
            .Inc(x => x.Counter, delta)
            .Set(x => x.LastUpdatedAt, nowMs);
        var options = new UpdateOptions { IsUpsert = true };

        try
        {
            await self.Progress.UpdateOneAsync(filter, update, options);
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            // 并发两次首次 +1 撞 _id 主键:重试一次走 update 分支(此时文档已存在,$inc 累加)。
            // 单次重试足够;再撞说明 MongoDB 异常,让外层 catch 兜底。
            await self.Progress.UpdateOneAsync(filter, update, options);
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await self.Progress.UpdateOneAsync(filter, update, options);
        }
    }

    // ── 判达标 + 抢占周期键 + 发邮件 ─────────────────────────────

    /// <summary>
    /// 判达标 → 抢占本周期键(原子)→ 抢占成功调 32 SendMailTo 投活动结算邮件(claim-then-act,设计 §3.4)。
    /// 抢占失败 = 本周期已发(并发 / 重启 / 同日重复触发)→ 跳过、不发(SV4/SV6/SV7)。
    /// reward=0 → 仅记已发不投邮件(OneShot 永发类一次性活动用,设计 §3.4 + §SV8)。
    /// MongoDB 不可达 → 静默跳过(沿 BLOCKED-env 基线)。
    /// </summary>
    public static async FTask EvaluateAndClaim(
        ActivityServiceComponent self, MailServiceComponent? mail, string account, ActivityDefDoc def, long nowMs)
    {
        if (self.Progress == null)
        {
            return;
        }

        // 1. 读进度。无记录(理论上 Increment 已 upsert,这里保险)→ 视作 counter=0 / lastClaimedCycleKey=0。
        var uniqueKey = BuildKey(account, def.ActivityId);
        var findFilter = Builders<ActivityProgressDoc>.Filter.Eq(x => x.UniqueKey, uniqueKey);
        var progress = await self.Progress.Find(findFilter).FirstOrDefaultAsync();
        var counter = progress?.Counter ?? 0L;
        var lastKey = progress?.LastClaimedCycleKey ?? 0L;

        // 2. 算本周期键。Always 类或周期键算不出 → 不发(本子单不取 Always)。
        var periodKey = ComputeCurrentCycleKey(def.Cycle, nowMs);
        if (periodKey == null)
        {
            return;
        }

        // 3. 判达标条件:counter ≥ target 且 lastClaimedCycleKey < 本周期键。
        if (counter < def.Target)
        {
            return; // 未达标(本周期未刷够)。
        }
        if (lastKey >= periodKey.Value)
        {
            return; // 本周期已发(同日 / 同周重复触发)。
        }

        // 4. 抢占本周期键(原子条件写)。filter _id 匹配 且 LastClaimedCycleKey < 本周期键 → $set 本周期键。
        //    抢占失败(并发 / 已发)→ 跳过,不发(SV6)。
        if (!await TryClaimCycleKey(self.Progress, uniqueKey, periodKey.Value, nowMs))
        {
            return;
        }

        // 5. 抢占成功:发活动结算邮件(claim-then-act,§3.4 关键)。
        //    reward=0 → 仅记已发不投邮件(OneShot 类活动场景);其他 → 调 32 SendMailTo 投定向邮件。
        if (def.Reward == 0)
        {
            Log.Info($"ActivityEvalHelper: account={account} activity_id={def.ActivityId} 本周期已发(无奖,仅记标记)。periodKey={periodKey.Value}");
            return;
        }

        if (mail == null)
        {
            // 抢占已成功但邮件服务未就绪 → 漏发窄窗(运营可补,设计 §四诚实取舍)。
            Log.Warning($"ActivityEvalHelper: account={account} activity_id={def.ActivityId} 本周期已抢占但 MailServiceComponent 未就绪,漏发(运营可补)。");
            return;
        }

        var mailIdOrNull = await MailDecisionHelper.SendMailTo(
            mail, account, def.SenderTextId, def.TitleTextId, def.ContentTextId, def.ExpireDays, def.Reward);
        if (mailIdOrNull == null)
        {
            // SendMailTo 返 null = mail.Directed 未就绪。同样属漏发窄窗(运营可补)。
            Log.Warning($"ActivityEvalHelper: account={account} activity_id={def.ActivityId} SendMailTo 返 null(Mail 未就绪),漏发(运营可补)。");
            return;
        }

        Log.Info($"ActivityEvalHelper: account={account} activity_id={def.ActivityId} 本周期发奖完成,mailId={mailIdOrNull},reward={def.Reward},periodKey={periodKey.Value}");
    }

    /// <summary>
    /// 抢占本周期键:仅当存量 LastClaimedCycleKey &lt; periodKey 才占位成功(原子条件写)。
    ///   - 文档已存在(Increment 已 upsert)且存量 &lt; periodKey → 原子更新 → 抢占成功。
    ///   - 存量 LastClaimedCycleKey &gt;= periodKey → filter 不匹配 → MatchedCount=0 → 抢占失败(本周期已发)。
    /// 并发两次只一次抢占成功(SV6);持久 MongoDB 跨会话/重启(SV7)。
    /// 同 33 TryClaimSettlePeriod 范式,但 IsUpsert=false:Increment 已确保文档存在(本子单 OnLogin 路径先 Increment 后 EvaluateAndClaim);
    /// 若文档因某种原因不存在 → MatchedCount=0 → 抢占失败、跳过(留待下次登录由 Increment 建文档后再抢占)。
    /// </summary>
    private static async FTask<bool> TryClaimCycleKey(
        IMongoCollection<ActivityProgressDoc> progress, string uniqueKey, long periodKey, long nowMs)
    {
        var filter = Builders<ActivityProgressDoc>.Filter.And(
            Builders<ActivityProgressDoc>.Filter.Eq(x => x.UniqueKey, uniqueKey),
            Builders<ActivityProgressDoc>.Filter.Lt(x => x.LastClaimedCycleKey, periodKey));
        var update = Builders<ActivityProgressDoc>.Update
            .Set(x => x.LastClaimedCycleKey, periodKey)
            .Set(x => x.LastUpdatedAt, nowMs);
        var options = new UpdateOptions { IsUpsert = false };
        var result = await progress.UpdateOneAsync(filter, update, options);
        return result.MatchedCount > 0;
    }

    // ── 周期键算法(设计 §3.3) ──────────────────────────────────

    /// <summary>
    /// 算本周期键(同 33 ComputeWeeklyDueMs 周期口径,但适配活动 Daily/Weekly/OneShot):
    ///   - Daily   = 今日 0:00 UTC ms(每天换新键);
    ///   - Weekly  = 本周一 0:00 UTC ms(每周换新键);
    ///   - OneShot = 1 常量(永发一次性,首次抢占后 LastClaimedCycleKey=1,之后永远 ≥ 1 不再发);
    ///   - 其他(含 Always) → null(本子单不取 Always)。
    /// 服务端时区 = UTC(同 33 §3.1 / 22 §3.5.1 周循环口径),不依赖服务端机器本地时区。
    /// </summary>
    public static long? ComputeCurrentCycleKey(int cycle, long nowMs)
    {
        switch (cycle)
        {
            case CycleDaily:
            {
                var now = nowMs.Transition();   // Unix ms → UTC DateTime。
                var today = now.Date;           // 今日 0:00 UTC。
                return today.Transition();      // 转回 Unix ms。
            }

            case CycleWeekly:
            {
                var now = nowMs.Transition();
                var nowDate = now.Date;
                // .NET DayOfWeek:Sunday=0..Saturday=6;转 ISO 1=周一..7=周日(同 RankSettleHelper.ComputeWeeklyDueMs)。
                var isoNowWeekday = ((int)nowDate.DayOfWeek + 6) % 7 + 1;
                var mondayDate = nowDate.AddDays(-(isoNowWeekday - 1));
                return mondayDate.Transition();
            }

            case CycleOneShot:
                return 1L;

            default:
                return null; // Always 类本子单不接(等价不防重)。
        }
    }

    // ── 工具 ────────────────────────────────────────────────────

    /// <summary>构造 activity_progress 的 _id 复合唯一键 "{account}_{activityId}"(同设计 39 §3.2)。</summary>
    private static string BuildKey(string account, int activityId)
    {
        return $"{account}_{activityId}";
    }
}
