using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 批量解锁上报请求入口(Outer RPC,运行在 Gate Scene)。
/// 传输层聚合:客户端一次事件补齐多解锁(升级跨多阈值 / 首登 backfill)时,把 N 条 C2G_UnlockCosmeticRequest
/// 收敛成 1 条,减少往返。语义 = CosmeticHelper.TryUnlockBatch:频率闸对整批只检一次,过闸后逐项 sanity + $addToSet
/// 幂等,逐项 sanity 失败跳过不阻断整批;解锁是集合幂等操作,回带处理后两个 kind 的最终解锁集(客户端投影以此覆盖)。
///
/// 玩家身份从会话取(同单条 C2G_UnlockCosmeticRequestHandler),不接受客户端上报账号。失败以 ResultCode 回包,
/// 不抛异常断连,框架 RPC ErrorCode 始终为 0。会话未登录 → NotLoggedIn。
/// </summary>
public sealed class C2G_UnlockCosmeticBatchRequestHandler
    : MessageRPC<C2G_UnlockCosmeticBatchRequest, G2C_UnlockCosmeticBatchResponse>
{
    protected override async FTask Run(Session session, C2G_UnlockCosmeticBatchRequest request,
        G2C_UnlockCosmeticBatchResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 UnlockCosmeticBatchRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)UnlockCosmeticResultCode.NotLoggedIn;
            return;
        }

        // 协议项 → 原语元组(Helper 保持 proto-agnostic,与单条 TryUnlock 同口径只吃 kind/id)。
        var items = new List<(int kind, int id)>(request.Items?.Count ?? 0);
        if (request.Items != null)
        {
            foreach (var it in request.Items)
            {
                items.Add((it.Kind, it.Id));
            }
        }

        var (resultCode, avatarIds, frameIds) =
            await CosmeticHelper.TryUnlockBatch(session.Scene, account, items);

        response.ResultCode = (int)resultCode;
        if (avatarIds != null)
        {
            response.UnlockedAvatarIds.AddRange(avatarIds);
        }
        if (frameIds != null)
        {
            response.UnlockedFrameIds.AddRange(frameIds);
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_UnlockCosmeticRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
