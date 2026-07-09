using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 清背包请求(Outer RPC,运行在 Gate Scene,调试用)。只清玩家背包两轨(堆叠轨 ItemHoldings +
/// 批次轨 ItemLots)、使用幂等锚 LastUseReqSeq 归 0、InventoryVersion 乐观版本 +1,不动货币 / 进度 / 其它字段。
///
/// 身份从会话取(GateAccountFlagComponent.Account.Name = accountId = PlayerDoc 主键),不接受客户端传账号,
/// 只清调用方自己,无跨玩家面。清空 + 推整份(空)背包快照(复用 SendInventoryDeltaPush,客户端 OnInventoryDeltaPush
/// 整份覆盖投影为空)。幂等:重复清同样刷成空。所有结果以 ResultCode 回包,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_ClearInventoryRequestHandler
    : MessageRPC<C2G_ClearInventoryRequest, G2C_ClearInventoryResponse>
{
    protected override async FTask Run(Session session, C2G_ClearInventoryRequest request,
        G2C_ClearInventoryResponse response, Action reply)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        if (account == null || string.IsNullOrEmpty(account.Name))
        {
            Log.Warning("收到 ClearInventoryRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = ClearInventoryResultCode.NotLoggedIn;
            return;
        }

        var accountName = account.Name;
        var (ok, doc) = await InventoryServiceHelper.ClearInventory(session.Scene, accountName);
        if (!ok)
        {
            response.ResultCode = ClearInventoryResultCode.ServiceUnavailable;
            return;
        }

        // 推整份(空)背包到该账号在线会话,客户端整份覆盖投影为空。doc==null(未首登)无可推,直接成功。
        if (doc != null)
        {
            InventoryServiceHelper.SendInventoryDeltaPush(session.Scene, accountName, doc);
        }

        response.ResultCode = ClearInventoryResultCode.Success;
        Log.Info($"ClearInventory 清背包成功 account={accountName}。");
    }
}
