using System;
using Fantasy.Async;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 发道具(Inner RPC,HttpGift 场景转发到 Gate 场景执行)。复用 InventoryServiceHelper.GrantTestItem 服务端权威发放
/// (限时/可堆叠按配置自动分轨),成功后推整份背包到该账号在线会话(在线玩家实时刷新)。
/// Response.Code:0=成功;非0=GrantTestItemResultCode 值或 GM 侧校验失败(-1 空账号)。
/// </summary>
public sealed class Http2G_GmGrantItemRequestHandler
    : AddressRPC<Scene, Http2G_GmGrantItemRequest, G2Http_GmGrantItemResponse>
{
    protected override async FTask Run(Scene scene, Http2G_GmGrantItemRequest request,
        G2Http_GmGrantItemResponse response, Action reply)
    {
        if (string.IsNullOrEmpty(request.AccountId))
        {
            response.Code = -1;
            response.Message = "empty accountId";
            return;
        }

        var (code, doc) = await InventoryServiceHelper.GrantTestItem(
            scene, request.AccountId, request.ItemId, request.Count);
        response.Code = (int)code;
        response.Message = code.ToString();

        if (code == GrantTestItemResultCode.Success && doc != null)
        {
            InventoryServiceHelper.SendInventoryDeltaPush(scene, request.AccountId, doc);
            Log.Info($"GM 发道具成功 account={request.AccountId} item={request.ItemId} count={request.Count}");
        }
        else
        {
            // 失败,或发放已落库但发后读整份文档瞬时失败(doc==null,无法推送):留痕便于运维定位。
            Log.Warning($"GM 发道具未完成 account={request.AccountId} item={request.ItemId} count={request.Count} code={code} pushed={doc != null}");
        }
    }
}
