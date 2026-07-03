using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 女神满档领取奖励请求入口(Outer RPC,运行在 Gate Scene)。
///
/// 玩家身份从会话取(同 C2G_PropertyChangeRequestHandler / C2G_DeliverOrderRequestHandler):
/// 读 GateAccountFlagComponent → Account.Name = UUID。请求不带账号 / 不带奖励内容(服务端按订单态 + 表自定 — 反作弊红线)。
/// 满档校验(原子 CAS)/ 清零 / 奖励派生由 GoddessClaimServiceHelper.TryClaim 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
///
/// 奖励元素实际入客户端合成区由客户端收到响应后 AddDirect 执行,本响应只回带「发什么」(元素类型 + 各档等级/数量)。
/// </summary>
public sealed class C2G_GoddessClaimRequestHandler : MessageRPC<C2G_GoddessClaimRequest, G2C_GoddessClaimResponse>
{
    protected override async FTask Run(Session session, C2G_GoddessClaimRequest request,
        G2C_GoddessClaimResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 GoddessClaimRequest 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = GoddessClaimResultCode.NotLoggedIn;
            return;
        }

        var (code, elementType, rewards) = await GoddessClaimServiceHelper.TryClaim(session.Scene, account);
        response.ResultCode = code;
        response.ElementType = elementType;

        if (code == GoddessClaimResultCode.Success)
        {
            foreach (var (level, count) in rewards)
            {
                var item = GoddessRewardItem.Create();
                item.Level = level;
                item.Count = count;
                response.Rewards.Add(item);
            }
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_DeliverOrderRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
