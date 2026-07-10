using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 发背包测试道具请求(Outer RPC,运行在 Gate Scene,调试用)。按 itemId 给调用方自己发 count 个道具 ——
/// 限时道具(TbItemDef.Term != 0)追加一条批次、可堆叠道具(Term == 0)累加持有,发后推整份背包快照
/// (复用 SendInventoryDeltaPush,客户端 OnInventoryDeltaPush 整份覆盖投影)。
///
/// 身份从会话取(GateAccountFlagComponent.Account.Name = accountId = PlayerDoc 主键),不接受客户端传账号,只发给自己,无跨玩家面。
/// 开发者调试入口,只做基本合法性校验(itemId 存在 + count 有界),不设反作弊。所有结果以 ResultCode 回包,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_GrantTestItemRequestHandler
    : MessageRPC<C2G_GrantTestItemRequest, G2C_GrantTestItemResponse>
{
    protected override async FTask Run(Session session, C2G_GrantTestItemRequest request,
        G2C_GrantTestItemResponse response, Action reply)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        if (account == null || string.IsNullOrEmpty(account.Name))
        {
            Log.Warning("收到 GrantTestItemRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = GrantTestItemResultCode.NotLoggedIn;
            return;
        }

        var accountName = account.Name;
        var (code, doc) = await InventoryServiceHelper.GrantTestItem(
            session.Scene, accountName, request.ItemId, request.Count);
        response.ResultCode = code;
        if (code != GrantTestItemResultCode.Success)
        {
            return;
        }

        // 发放成功:推整份背包到该账号在线会话,客户端整份覆盖投影。
        if (doc != null)
        {
            InventoryServiceHelper.SendInventoryDeltaPush(session.Scene, accountName, doc);
        }
        else
        {
            // 发放已落库(权威态正确),但发后读整份文档瞬时失败(Mongo 抖动)时拿不到文档、无法推送:
            // 客户端投影本次不刷新,待下次自然推送 / 重登对齐。非丢数据,告警留痕便于定位。
            Log.Warning($"GrantTestItem 发放成功但读文档失败,跳过背包推送(客户端投影暂不刷新) account={accountName} item={request.ItemId}。");
        }
        Log.Info($"GrantTestItem 发测试道具成功 account={accountName} item={request.ItemId} count={request.Count}。");
    }
}
