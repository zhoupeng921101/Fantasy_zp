using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 拉邮件列表入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV9):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name,
/// 请求无业务字段、不接受客户端自报账号(协议 C2G_MailListRequest 无账号字段)。
/// 返回该账号应收(活跃广播 + 该账号定向)且未过期的邮件 + 每封领取态。
/// 所有结果以 ResultCode 回包,不抛异常断连(SV11);框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/32-mail-server.md §三/§8.1。
/// </summary>
public sealed class C2G_MailListRequestHandler : MessageRPC<C2G_MailListRequest, G2C_MailListResponse>
{
    protected override async FTask Run(Session session, C2G_MailListRequest request,
        G2C_MailListResponse response, Action reply)
    {
        // 身份从会话取(SV9):非请求参数。会话未登录(无账号标记)则无法建立身份 → 服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到拉邮件列表但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = MailClaimResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<MailServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 MailServiceComponent,无法拉邮件列表。");
            response.ResultCode = MailClaimResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, mails) = await MailDecisionHelper.List(service, account);
        response.ResultCode = resultCode;
        response.Mails = mails;

        Log.Debug($"拉邮件列表 account={account} result={resultCode} mailCount={mails.Count}");
    }

    /// <summary>从会话取登录时绑定的账号名。无登录标记返回 null。</summary>
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
