using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 换装请求入口(Outer RPC,运行在 Gate Scene,云存档 blob 退役·第 2 批·子批 2c)。
///
/// 玩家身份从会话取(同 C2G_RenameRequestHandler):读 GateAccountFlagComponent → Account.Name = accountId。
/// 请求只带 Kind + Id,**不**接受客户端上报账号。服务端校验目标 id 已在对应已解锁集合内(未解锁拒),
/// 通过则原子 $set 当前佩戴 id。失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终为 0。
/// 响应回带最新当前头像 id + 当前框 id 供客户端对账。
/// </summary>
public sealed class C2G_EquipCosmeticRequestHandler
    : MessageRPC<C2G_EquipCosmeticRequest, G2C_EquipCosmeticResponse>
{
    protected override async FTask Run(Session session, C2G_EquipCosmeticRequest request,
        G2C_EquipCosmeticResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 EquipCosmeticRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)EquipCosmeticResultCode.NotLoggedIn;
            response.CurrentAvatarId = 0;
            response.CurrentFrameId = 0;
            return;
        }

        var (resultCode, currentAvatarId, currentFrameId) =
            await CosmeticHelper.TryEquip(session.Scene, account, request.Kind, request.Id);

        response.ResultCode = (int)resultCode;
        response.CurrentAvatarId = currentAvatarId;
        response.CurrentFrameId = currentFrameId;
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_RenameRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
