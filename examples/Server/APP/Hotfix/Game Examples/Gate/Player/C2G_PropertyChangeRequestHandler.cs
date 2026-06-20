using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 玩家属性变更请求入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV6/SV7):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name(= UUID),
/// 请求只携带类型 + delta + reason,**不接受**客户端自报账号、**不接受**客户端传入绝对余额
/// (反作弊红线,§3.3.2 + §5.5,Code Review SV17 必拦)。
/// 校验 + 写库 + 推送由 PlayerPropertyServiceHelper.ChangeProperty 统一执行(与服务端进程内 API 共用一套);
/// 所有结果(含失败 / 边界)以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/37-player-attr-server.md §3.3.2 + §3.4 + §3.5。
/// </summary>
public sealed class C2G_PropertyChangeRequestHandler : MessageRPC<C2G_PropertyChangeRequest, G2C_PropertyChangeResponse>
{
    protected override async FTask Run(Session session, C2G_PropertyChangeRequest request,
        G2C_PropertyChangeResponse response, Action reply)
    {
        // 回声请求的类型(便于客户端段下一刀路由更新到对应字段)。
        response.Type = request.Type;

        // 身份从会话取:非请求参数(SV17 红线 ②)。会话未登录(无账号标记)= 35 链路未走完 → NotLoggedIn。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 PropertyChangeRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = PropertyChangeResultCode.NotLoggedIn;
            return;
        }

        var reason = request.Reason ?? string.Empty;
        var (resultCode, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
            session.Scene, account, request.Type, request.Delta, reason);

        response.ResultCode = resultCode;
        response.NewAmount = newAmount;

        // 仅成功时起推送 — 推送目标 = 该 UUID 在线全部会话(包括触发会话本身,§3.3.3 + §5.4)。
        // 失败分支(NotEnough / OverLimit / UnknownType / InvalidRequest / ServiceUnavailable)不推送(SV8/SV9)。
        if (resultCode == PropertyChangeResultCode.Success)
        {
            PlayerPropertyServiceHelper.SendDeltaPushTo(session.Scene, account, request.Type, newAmount, reason);
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同 redeem / mail handler 范式)。无登录标记返回 null。</summary>
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
