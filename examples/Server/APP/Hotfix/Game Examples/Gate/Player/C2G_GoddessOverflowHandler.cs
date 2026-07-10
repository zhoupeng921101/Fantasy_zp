using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 女神奖励盘面溢出入库请求入口(Outer RPC,运行在 Gate Scene)。
/// 身份从会话取(读登录时挂载的 GateAccountFlagComponent → Account.Name = UUID),
/// 请求只带元素道具 id + 数量 + reqSeq,不接受客户端自报账号(反作弊红线,同 UseItem / PropertyChange handler)。
/// 校验(itemId ∈ 元素道具集 + 限界信任幅度)+ 幂等落账由 GoddessBagServiceHelper.GrantOverflow 统一裁决;
/// 结果以 ResultCode 回包,不抛异常断连(框架 RPC ErrorCode 始终 0)。成功后背包整份推送(客户端刷新投影)。
/// </summary>
public sealed class C2G_GoddessOverflowHandler : MessageRPC<C2G_GoddessOverflow, G2C_GoddessOverflowResponse>
{
    protected override async FTask Run(Session session, C2G_GoddessOverflow request, G2C_GoddessOverflowResponse response, Action reply)
    {
        response.ItemId = request.ItemId;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 GoddessOverflow 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = GoddessBagResultCode.NotLoggedIn;
            return;
        }

        var result = await GoddessBagServiceHelper.GrantOverflow(session.Scene, account, request.ItemId, request.Count, request.ReqSeq, request.Nonce);
        response.ResultCode = result.Code;
        response.NewCount = result.NewCount;

        if (result.Code == GoddessBagResultCode.Success && result.DocAfter != null)
        {
            // 背包整份推送(客户端据此刷新背包投影;含首次成功与幂等重发的回推,纠正客户端可能的乐观偏差)。
            InventoryServiceHelper.SendInventoryDeltaPush(session.Scene, account, result.DocAfter);
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同 UseItem handler 范式)。无登录标记返回 null。</summary>
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
