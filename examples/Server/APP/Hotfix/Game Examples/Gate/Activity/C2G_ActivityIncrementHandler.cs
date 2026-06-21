using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// Cumulative 节律累加入口(Outer RPC,运行在 Gate Scene)。Tier 4 第 4 子单首次新建客户端可触发的活动系统 RPC。
///
/// 玩家身份从会话取(SV12 ①):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name(= UUID),
/// 请求**不**携带账号字段(协议层即已不预留);即使协议被改坏夹带账号字段、handler 也只信会话身份(沿设计 30/31/32/45 同范式)。
///
/// handler 校验链(设计 47 §3.3,按此顺序短路):
///   1. 身份从会话取:无 GateAccountFlagComponent / Account.Name → ServiceUnavailable(沿 32/45 同口径,不另设 NotLoggedIn 码);
///   2. delta &gt; 0:&lt;= 0 → InvalidRequest(SV6);
///   3. activityId 存在(读 DefCache):不存在 → InvalidRequest(SV6);
///   4. 配置 type=Cumulative:不是 → NotCumulative(SV5,防客户端用此 RPC 推 Login / Schedule / Action 类活动);
///   5. delta 上限钳制:&gt; 10000 → 默默钳为 10000 不报错(SV7,服务端权威防客户端推爆 counter)。
///
/// 校验通过后调 ActivityProgressService.IncrementAsync 走完整 service 路径(handler / service 统一逻辑路径,
/// 不复制两份发奖编排,沿设计 47 §3.5 + SV12 ③);service 层兜底再校验 type=Cumulative(双层防御 SV12 ②)。
///
/// 所有失败以 ResultCode 回包,不抛异常断连(SV12);框架 RPC ErrorCode 始终保持 0。
///
/// 设计基线:design-docs/47-activity-cumulative.md §3.2 / §3.3。
/// </summary>
public sealed class C2G_ActivityIncrementHandler : MessageRPC<C2G_ActivityIncrement, G2C_ActivityIncrementResponse>
{
    /// <summary>delta 服务端单次钳制上限(设计 47 §3.3 decisions §4,plan O3 后续可扩 activity.xlsx 字段运营可配)。</summary>
    private const int MaxDeltaPerCall = 10000;

    protected override async FTask Run(Session session, C2G_ActivityIncrement request,
        G2C_ActivityIncrementResponse response, Action reply)
    {
        // 1. 身份从会话取(SV12 ①):非请求参数。会话未登录(无账号标记)= 35 链路未走完 / 会话异常 → ServiceUnavailable
        //    (沿 32 邮件 handler 同范式;不引入「NotLoggedIn」结果码,与设计 47 §3.2 错误码集 4 个保持一致)。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 ActivityIncrement 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = ActivityIncrementResultCode.ServiceUnavailable;
            response.CurrentCounter = 0L;
            response.TargetReached = false;
            return;
        }

        // 2. delta > 0(SV6)。
        if (request.Delta <= 0)
        {
            response.ResultCode = ActivityIncrementResultCode.InvalidRequest;
            response.CurrentCounter = 0L;
            response.TargetReached = false;
            return;
        }

        // 3-4. 取活动服务组件 + 校验 activityId 配置存在 + 校验 type=Cumulative(handler 边界守门,service 层兜底再校一次)。
        var activitySvc = session.Scene.GetComponent<ActivityServiceComponent>();
        if (activitySvc == null || activitySvc.DefCache.Count == 0)
        {
            Log.Warning($"ActivityServiceComponent 未挂载 / 配置缓存空,无法处理 ActivityIncrement。account={account} activityId={request.ActivityId}");
            response.ResultCode = ActivityIncrementResultCode.ServiceUnavailable;
            response.CurrentCounter = 0L;
            response.TargetReached = false;
            return;
        }

        // activityId 存在性校验(handler 顺序:先存在再 type,沿设计 47 §3.3 顺序声明 + SV12 ⑥)。
        //   存在性失败 → InvalidRequest(归一,不泄露「该 id 在配但 type 不对」信息差)。
        if (!activitySvc.DefCache.TryGetValue(request.ActivityId, out var def))
        {
            response.ResultCode = ActivityIncrementResultCode.InvalidRequest;
            response.CurrentCounter = 0L;
            response.TargetReached = false;
            return;
        }

        // type 校验(防客户端用此 RPC 推 Login / Schedule / Action 类活动 counter,旁路登录类节律)。
        if (def.Type != ActivityEvalHelper.TypeCumulative)
        {
            Log.Warning($"客户端试图用 ActivityIncrement 推非 Cumulative 类活动:account={account} activityId={request.ActivityId} type={def.Type}(预期 Cumulative=2),拒。");
            response.ResultCode = ActivityIncrementResultCode.NotCumulative;
            response.CurrentCounter = 0L;
            response.TargetReached = false;
            return;
        }

        // 5. delta 上限钳制(SV7):超上限默默钳为 10000 不报错(降级语义,沿 45 limit 钳制同范式);
        //    客户端从 response.CurrentCounter 自查实际写入值。
        var clampedDelta = request.Delta > MaxDeltaPerCall ? MaxDeltaPerCall : request.Delta;

        // 6. 调 service 共用同一逻辑路径(handler / service 不复制发奖编排,沿设计 47 §3.5 + SV12 ③)。
        var mailSvc = session.Scene.GetComponent<MailServiceComponent>();
        var (resultCode, currentCounter, targetReached) = await ActivityProgressService.IncrementAsync(
            activitySvc, mailSvc, account, request.ActivityId, clampedDelta, Fantasy.Helper.TimeHelper.Now);

        response.ResultCode = resultCode;
        response.CurrentCounter = currentCounter;
        response.TargetReached = targetReached;

        Log.Debug($"ActivityIncrement account={account} activityId={request.ActivityId} delta={request.Delta}(clamped={clampedDelta}) " +
                  $"result={resultCode} currentCounter={currentCounter} targetReached={targetReached}");
    }

    /// <summary>从会话取登录时绑定的账号名(同 mail / property handler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null)
        {
            return null;
        }

        Account account = flag.Account;
        return account?.Name;
    }
}
