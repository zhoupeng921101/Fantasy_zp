using System;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 排行榜每日/点赞奖领取入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(不接受客户端自报账号),请求只携带榜 id + 领取类型(1=每日/2=点赞)。
/// 服务端权威判资格(每日=在榜+名次档有每日奖;点赞=榜级有点赞奖)+ 防同日重领 + 经 SendMailTo 投奖励邮件;
/// 结果(含失败)以 ResultCode 回包,不抛异常断连。替代客户端本地自发奖(data-authority:领取态与发奖均服务端权威)。
/// </summary>
public sealed class C2G_RankClaimRewardRequestHandler : MessageRPC<C2G_RankClaimRewardRequest, G2C_RankClaimRewardResponse>
{
    protected override async FTask Run(Session session, C2G_RankClaimRewardRequest request,
        G2C_RankClaimRewardResponse response, Action reply)
    {
        // 身份从会话取:非请求参数。会话未登录 → 无法确定身份,服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到排行榜领奖但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = RankClaimResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<RankServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 RankServiceComponent,无法领取排行榜奖。");
            response.ResultCode = RankClaimResultCode.ServiceUnavailable;
            return;
        }

        response.ResultCode = await RankClaimHelper.Claim(
            service, account, request.RankId, request.ClaimType, TimeHelper.Now);

        Log.Debug($"排行榜领奖 account={account} rankId={request.RankId} type={request.ClaimType} result={response.ResultCode}");
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
