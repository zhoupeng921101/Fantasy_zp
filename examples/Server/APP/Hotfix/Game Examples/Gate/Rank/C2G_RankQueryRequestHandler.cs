using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 排行榜查榜入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV8):读会话上 GateAccountFlagComponent → Account.Name 算「我的名次」,
/// 请求只携带榜 id,不接受客户端自报账号(协议 C2G_RankQueryRequest 无账号字段),只能查自己名次。
/// 所有结果(含失败)以 ResultCode 回包,不抛异常断连(SV10);框架 RPC ErrorCode 始终保持 0。
/// 设计基线:design-docs/31-rank-server.md §三/§8.1。
/// </summary>
public sealed class C2G_RankQueryRequestHandler : MessageRPC<C2G_RankQueryRequest, G2C_RankQueryResponse>
{
    protected override async FTask Run(Session session, C2G_RankQueryRequest request,
        G2C_RankQueryResponse response, Action reply)
    {
        // 身份从会话取(SV8):非请求参数。会话未登录则无法算「我的名次」 → 服务不可用。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到排行榜查询但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = RankQueryResultCode.ServiceUnavailable;
            return;
        }

        var service = session.Scene.GetComponent<RankServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 RankServiceComponent,无法查询排行榜。");
            response.ResultCode = RankQueryResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, entries, myRank, myScore) = await RankDecisionHelper.Query(service, account, request.RankId);
        response.ResultCode = resultCode;
        // 仅成功时附条目列表;失败分支条目列表保持空(响应 Entries 自动初始化为空 List)。
        if (resultCode == RankQueryResultCode.Success)
        {
            response.Entries = entries;
        }
        response.MyRank = myRank;
        response.MyScore = myScore;

        Log.Debug($"排行榜查询 account={account} rankId={request.RankId} result={resultCode} entryCount={entries.Count} myRank={myRank} myScore={myScore}");
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
