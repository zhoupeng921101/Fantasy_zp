using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 女神元素道具取回请求入口(Outer RPC,运行在 Gate Scene)。
/// 玩法界面双击背包元素道具「用掉一个」触发:身份从会话取(GateAccountFlagComponent → Account.Name),
/// 请求只带元素道具 id + reqSeq,不接受客户端自报账号(反作弊红线)。
/// 校验(itemId ∈ 元素道具集 + 持有 ≥ 1)+ 幂等 -1 由 GoddessBagServiceHelper.RetrieveElement 统一裁决;
/// 结果以 ResultCode 回包,不抛异常断连。成功后背包整份推送;客户端拿 Success 再把元素飞回盘面。
/// </summary>
public sealed class C2G_RetrieveElementHandler : MessageRPC<C2G_RetrieveElement, G2C_RetrieveElementResponse>
{
    protected override async FTask Run(Session session, C2G_RetrieveElement request, G2C_RetrieveElementResponse response, Action reply)
    {
        response.ItemId = request.ItemId;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 RetrieveElement 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = GoddessBagResultCode.NotLoggedIn;
            return;
        }

        var result = await GoddessBagServiceHelper.RetrieveElement(session.Scene, account, request.ItemId, request.ReqSeq);
        response.ResultCode = result.Code;
        response.NewCount = result.NewCount;

        if (result.Code == GoddessBagResultCode.Success && result.DocAfter != null)
        {
            // 背包整份推送(含首次成功与幂等重发回推)。
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
