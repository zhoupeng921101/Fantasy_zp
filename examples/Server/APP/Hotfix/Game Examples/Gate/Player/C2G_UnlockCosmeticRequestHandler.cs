using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 解锁上报请求入口(Outer RPC,运行在 Gate Scene,云存档 blob 退役·第 2 批·子批 2c)。
///
/// client-report:客户端按等级配置算出解锁、上报 Kind + Id,服务端限界信任 sanity(id 落合法段 + 集合大小上限
/// + 频率闸)后 $addToSet 幂等加入对应已解锁集合(重复上报同 id 无副作用)。饰品低危,不在服务端建整套头像等级配置自算。
///
/// 玩家身份从会话取(同 C2G_RenameRequestHandler),**不**接受客户端上报账号。失败以 ResultCode 回包,
/// 不抛异常断连,框架 RPC ErrorCode 始终为 0。响应回带更新后的对应集合供客户端对账。
/// </summary>
public sealed class C2G_UnlockCosmeticRequestHandler
    : MessageRPC<C2G_UnlockCosmeticRequest, G2C_UnlockCosmeticResponse>
{
    protected override async FTask Run(Session session, C2G_UnlockCosmeticRequest request,
        G2C_UnlockCosmeticResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 UnlockCosmeticRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)UnlockCosmeticResultCode.NotLoggedIn;
            return;
        }

        var (resultCode, unlockedIds) =
            await CosmeticHelper.TryUnlock(session.Scene, account, request.Kind, request.Id);

        response.ResultCode = (int)resultCode;
        if (unlockedIds != null)
        {
            response.UnlockedIds.AddRange(unlockedIds);
        }
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
