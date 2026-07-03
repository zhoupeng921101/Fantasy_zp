using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 使用道具请求入口(Outer RPC,运行在 Gate Scene)。
/// 身份从会话取(读登录时挂载的 GateAccountFlagComponent → Account.Name = UUID),
/// 请求只携带道具 id + 数量 + reqSeq,不接受客户端自报账号(反作弊红线,同 PropertyChange handler)。
/// 校验 + 扣道具 + 产出 + 幂等由 InventoryServiceHelper.UseItem 统一裁决;所有结果以 ResultCode 回包,不抛异常断连。
/// 成功后:背包整份推送(客户端刷新投影)+ 产出货币的属性推送(客户端刷新余额)。
/// </summary>
public sealed class C2G_UseItemRequestHandler : MessageRPC<C2G_UseItem, G2C_UseItemResponse>
{
    protected override async FTask Run(Session session, C2G_UseItem request, G2C_UseItemResponse response, Action reply)
    {
        response.ItemId = request.ItemId;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 UseItem 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = UseItemResultCode.NotLoggedIn;
            return;
        }

        var result = await InventoryServiceHelper.UseItem(session.Scene, account, request.ItemId, request.Count, request.ReqSeq);
        response.ResultCode = result.Code;
        response.ConsumedCount = result.ConsumedCount;
        if (result.HasProduce)
        {
            var produced = PropertyAmount.Create();
            produced.Type = result.ProducedType;
            produced.Amount = result.ProducedAmountTotal;
            response.Produced.Add(produced);
        }

        if (result.Code == UseItemResultCode.Success)
        {
            // 背包整份推送(客户端据此刷新背包投影;response 只带消耗/产出量,不带整份背包)。
            InventoryServiceHelper.SendInventoryDeltaPush(session.Scene, account, result.DocAfter);
            // 产出货币的属性推送(客户端刷新余额;绝对值,与 response.Produced 一致,幂等)。
            if (result.HasProduce)
            {
                PlayerPropertyServiceHelper.SendDeltaPushTo(
                    session.Scene, account, result.ProducedType, result.ProducedNewBalance, $"item_use:item{request.ItemId}");
            }
        }
        else if (result.Code == UseItemResultCode.Duplicate && result.DocAfter != null)
        {
            // 幂等重发(首次已成功、但推送/ack 弱网丢失):回推权威背包整份,纠正客户端可能残留的乐观偏差。
            InventoryServiceHelper.SendInventoryDeltaPush(session.Scene, account, result.DocAfter);
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同 PropertyChange handler 范式)。无登录标记返回 null。</summary>
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
