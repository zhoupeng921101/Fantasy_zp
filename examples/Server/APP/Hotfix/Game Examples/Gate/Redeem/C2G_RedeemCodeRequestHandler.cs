using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 兑换码裁决入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV9):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name,
/// 请求只携带码字符串,不接受客户端自报账号(协议 C2G_RedeemCodeRequest 无账号字段)。
/// 所有结果(含失败)以 ResultCode 回包,不抛异常断连(SV10);框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/30-redeem-code-server.md §三/§8.1。
/// </summary>
public sealed class C2G_RedeemCodeRequestHandler : MessageRPC<C2G_RedeemCodeRequest, G2C_RedeemCodeResponse>
{
    protected override async FTask Run(Session session, C2G_RedeemCodeRequest request,
        G2C_RedeemCodeResponse response, Action reply)
    {
        // 身份从会话取(SV9 / CV8):非请求参数。会话未登录(无账号标记)则无法建立身份 → 服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到兑换请求但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = RedeemResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<RedeemServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 RedeemServiceComponent,无法裁决兑换。");
            response.ResultCode = RedeemResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, rewardBoxId) = await RedeemDecisionHelper.Redeem(service, account, request.Code);
        response.ResultCode = resultCode;
        // 仅成功时附奖励盒 id(服务端已权威发奖 + 推送,客户端读 TbRewardBox 展示内容);失败分支保持 0。
        if (resultCode == RedeemResultCode.Success)
        {
            response.RewardBoxId = rewardBoxId;
        }

        Log.Debug($"兑换裁决 account={account} rawCode='{request.Code}' result={resultCode} rewardBox={rewardBoxId}");
    }

    /// <summary>
    /// 从会话取登录时绑定的账号名。无登录标记返回 null。
    /// </summary>
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
