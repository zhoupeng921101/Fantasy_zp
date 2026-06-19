using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 排行榜上报裁决入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV8):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name,
/// 请求只携带榜 id + 分数,不接受客户端自报账号(协议 C2G_RankSubmitScoreRequest 无账号字段)。
/// 所有结果(含失败/边界)以 ResultCode 回包,不抛异常断连(SV10);框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/31-rank-server.md §三/§8.1。
/// </summary>
public sealed class C2G_RankSubmitScoreRequestHandler : MessageRPC<C2G_RankSubmitScoreRequest, G2C_RankSubmitScoreResponse>
{
    protected override async FTask Run(Session session, C2G_RankSubmitScoreRequest request,
        G2C_RankSubmitScoreResponse response, Action reply)
    {
        // 身份从会话取(SV8):非请求参数。会话未登录(无账号标记)则无法建立身份 → 服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到排行榜上报但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = RankSubmitResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<RankServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 RankServiceComponent,无法裁决排行榜上报。");
            response.ResultCode = RankSubmitResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, bestScore) = await RankDecisionHelper.Submit(service, account, request.RankId, request.Score);
        response.ResultCode = resultCode;
        response.BestScore = bestScore;

        Log.Debug($"排行榜上报 account={account} rankId={request.RankId} score={request.Score} result={resultCode} best={bestScore}");
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
