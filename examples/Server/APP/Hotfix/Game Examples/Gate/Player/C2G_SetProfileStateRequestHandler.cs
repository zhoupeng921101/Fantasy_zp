using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 设置玩家档案状态请求入口(Outer RPC,运行在 Gate Scene,原云存档 blob 退役·第 3 批·子批 3b)。
///
/// 皮肤态(SkinMono / SkinMonoId)+ 神庙装饰(TempleDecorated)三个「设置状态」一次性全量上报(SET 语义)。
/// 玩家身份从会话取(同 C2G_WishForEnergyRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID,不接受客户端上报账号。
/// sanity 校验 + 原子 $set 由 ProfileStateHelper.TrySet 统一执行;失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终保持 0。
/// 响应回带服务端当前权威三态供客户端对账。
/// </summary>
public sealed class C2G_SetProfileStateRequestHandler : MessageRPC<C2G_SetProfileStateRequest, G2C_SetProfileStateResponse>
{
    protected override async FTask Run(Session session, C2G_SetProfileStateRequest request,
        G2C_SetProfileStateResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 SetProfileStateRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)SetProfileStateResultCode.NotLoggedIn;
            response.SkinMono = 0;
            response.SkinMonoId = -1;
            response.TempleDecorated = 0L;
            return;
        }

        var (resultCode, skinMono, skinMonoId, templeDecorated) =
            await ProfileStateHelper.TrySet(session.Scene, account, request.SkinMono, request.SkinMonoId, request.TempleDecorated);

        response.ResultCode = (int)resultCode;
        response.SkinMono = skinMono;
        response.SkinMonoId = skinMonoId;
        response.TempleDecorated = templeDecorated;
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_WishForEnergyRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
