using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 领取邮件奖励入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV9):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name,
/// 请求只携带邮件标识,不接受客户端自报账号(协议 C2G_MailClaimRequest 无账号字段)。
/// 领取由服务端原子防重领 + 服务端按附件库 id 抽奖裁定附件 + 服务端时钟判过期。
/// 所有结果(含失败/边界)以 ResultCode 回包,不抛异常断连(SV11);框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/32-mail-server.md §三/§8.1。
/// </summary>
public sealed class C2G_MailClaimRequestHandler : MessageRPC<C2G_MailClaimRequest, G2C_MailClaimResponse>
{
    protected override async FTask Run(Session session, C2G_MailClaimRequest request,
        G2C_MailClaimResponse response, Action reply)
    {
        // 身份从会话取(SV9):非请求参数。会话未登录(无账号标记)则无法建立身份 → 服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到领取邮件但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = MailClaimResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<MailServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 MailServiceComponent,无法裁决领取。");
            response.ResultCode = MailClaimResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, rewards) = await MailDecisionHelper.Claim(service, account, request.MailId);
        response.ResultCode = resultCode;
        // 仅成功时附奖励列表;失败分支(已领过/已过期/邮件不存在/无奖励)奖励列表保持空(SV4)。
        if (resultCode == MailClaimResultCode.Success)
        {
            response.Rewards = rewards;
        }

        Log.Debug($"领取邮件 account={account} mailId='{request.MailId}' result={resultCode} rewardCount={rewards.Count}");
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
