using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// Cumulative 节律服务端进程内 API(Tier 4 第 4 子单兑现设计 39 §3.5 接缝)。
///
/// 调用方:
///   - Outer RPC handler:C2G_ActivityIncrementHandler 校验通过后调 IncrementAsync,
///     共用同一逻辑路径(handler / service 不复制两份发奖编排)。
///   - 服务端业务方(未来:运营 GM 工具 / 自动赠送 / 服务端拦截类系统事件)可直调 IncrementAsync,
///     绕开 handler 校验但走 service 层兜底 type 校验(双层防御,纵深防御未来扩展)。
///
/// 与 Login 节律(ActivityEvalHelper.OnLogin)对照:
///   - Login 节律:登录链路触发 → 遍历 type=Login 活动 → 各自 Increment(+1) + EvaluateAndClaim;
///   - Cumulative 节律:业务方推 (account, activityId, delta) → 单活动校验 type=Cumulative → Increment(+delta) + EvaluateAndClaim;
///   - 两路共用同一发奖编排(ActivityEvalHelper.EvaluateAndClaim,设计 39 §3.4 流程零改)。
///
/// 不变量(service 层兜底,纵深防御):
///   - 配置必存在 + type=Cumulative:防未来业务方误调推 Login / Schedule / Action 类活动 counter(handler 已校验,这里兜底);
///   - 不再做 delta ≤ 0 / 上限钳制:那是「客户端边界守门」的 handler 职责,service 假设入参已被 handler 钳;
///     若服务端业务方直调 service,delta 应由调用方语义保证(运营内部工具,可信)。
///
/// 设计基线:design-docs/47-activity-cumulative.md §3.5 service 行为契约。
/// </summary>
public static class ActivityProgressService
{
    /// <summary>
    /// Cumulative 节律累加入口(handler / 服务端业务方共用)。
    ///
    /// 返回 (resultCode, currentCounter, targetReached):
    ///   - resultCode:Success / NotCumulative / ServiceUnavailable(InvalidRequest 由 handler 层拦,service 不返此码);
    ///   - currentCounter:Success 时 = 写后 counter 值(供客户端 UI 显示);其它码取 0;
    ///   - targetReached:Success 时 = 本次是否首次达标 + 抢占 + 投奖;其它码取 false。
    ///
    /// 流程(沿设计 §3.5 mermaid 序列图):
    ///   1. 服务组件未就绪 / 配置未加载 → ServiceUnavailable(沿设计 §四 BLOCKED-env 基线;玩家观察 = 重连后再推);
    ///   2. 读 DefCache 取活动配置 → 不存在 → ServiceUnavailable(理论上 handler 已校验,这里防御性兜底,
    ///      不返 InvalidRequest 是因为 service 层语义边界是「业务方调用前已知 activityId 存在」,
    ///      运行期不存在 = 配置缓存与 handler 不同步 = 服务问题);
    ///   3. 配置 type ≠ Cumulative → NotCumulative(service 层兜底:防未来服务端业务方绕 handler 误调推 Login 类活动);
    ///   4. 调既有 ActivityEvalHelper.Increment($inc counter += delta + $set LastUpdatedAt,文档级原子);
    ///   5. 读写后 counter 值(直接 Find 一次,语义清晰过 ReturnDocument=After 二段写法,且 EvaluateAndClaim 紧随其后也读一次,
    ///      MongoDB driver 内部连接复用,两次 read 性能可接;若未来对性能极端敏感再优化);
    ///   6. 调既有 EvaluateAndClaim 判达标 + 抢占周期键 + 调 SendMailTo 发邮件,取其 bool 返回值作 targetReached;
    ///   7. 全程 catch MongoException → ServiceUnavailable + Warning,不抛致 handler 中断。
    /// </summary>
    public static async FTask<(ActivityIncrementResultCode resultCode, long currentCounter, bool targetReached)>
        IncrementAsync(ActivityServiceComponent? self, MailServiceComponent? mail, string account, int activityId, long delta, long nowMs)
    {
        // 1. 服务未就绪。
        if (self == null || self.Progress == null || self.DefCache.Count == 0)
        {
            Log.Warning($"ActivityProgressService.IncrementAsync: ActivityServiceComponent 未就绪(self/Progress/DefCache 任一 null/空),account={account} activityId={activityId}。");
            return (ActivityIncrementResultCode.ServiceUnavailable, 0L, false);
        }

        // 2. 配置存在性(防御性,handler 已校验)。
        if (!self.DefCache.TryGetValue(activityId, out var def))
        {
            Log.Warning($"ActivityProgressService.IncrementAsync: activityId={activityId} 配置缓存未命中(理论上 handler 已校验,服务问题)account={account}。");
            return (ActivityIncrementResultCode.ServiceUnavailable, 0L, false);
        }

        // 3. type 校验(service 层兜底,纵深防御,沿设计 47 §3.5 + SV12 ②)。
        if (def.Type != ActivityEvalHelper.TypeCumulative)
        {
            Log.Warning($"ActivityProgressService.IncrementAsync: activityId={activityId} type={def.Type} 非 Cumulative(=2),拒。account={account}。");
            return (ActivityIncrementResultCode.NotCumulative, 0L, false);
        }

        try
        {
            // 3.5 跨周期 counter 清零(Daily / Weekly Cumulative 前瞻支持,沿设计 47 §3.5 + SV12 ⑤;OneShot 跳过)。
            //     必须发生在 Increment 之前,避免「未清零先 +delta = 跨周期累计漂移」(沿 OnLogin Weekly 路径同范式)。
            await ActivityEvalHelper.ResetCounterIfCrossedPeriod(self, activityId, account, def.Cycle, nowMs);

            // 4. counter += delta(沿既有 ActivityEvalHelper.Increment $inc 原子,设计 47 §3.5 + SV8)。
            await ActivityEvalHelper.Increment(self, account, activityId, delta, nowMs);

            // 5. 读写后 counter(供回包 + 内联给 handler 看实际写入值)。
            var uniqueKey = $"{account}_{activityId}";
            var findFilter = Builders<ActivityProgressDoc>.Filter.Eq(x => x.UniqueKey, uniqueKey);
            var progress = await self.Progress.Find(findFilter).FirstOrDefaultAsync();
            var currentCounter = progress?.Counter ?? 0L;

            // 6. 判达标 + 抢占周期键 + 发邮件(取 bool 返回值作 targetReached,沿设计 47 §3.2)。
            var targetReached = await ActivityEvalHelper.EvaluateAndClaim(self, mail, account, def, nowMs);

            return (ActivityIncrementResultCode.Success, currentCounter, targetReached);
        }
        catch (MongoException e)
        {
            // MongoDB 抖动 / 不可达 → ServiceUnavailable,不抛(沿设计 47 §四 + 32 同口径)。
            Log.Warning($"ActivityProgressService.IncrementAsync: account={account} activityId={activityId} delta={delta} MongoDB 异常,返 ServiceUnavailable。{e.Message}");
            return (ActivityIncrementResultCode.ServiceUnavailable, 0L, false);
        }
    }
}
