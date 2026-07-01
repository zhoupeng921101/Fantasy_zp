using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 改名请求入口(Outer RPC,运行在 Gate Scene,云存档 blob 退役·第 2 批·子批 2a)。
///
/// 玩家身份从会话取(同 C2G_PropertyChangeRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求只带新昵称,**不**接受客户端上报费用 / 次数(反作弊红线:费用服务端按自己的 RenameCount 派生)。
/// 校验 / 算费 / 扣钻 / 原子写名由 RenameHelper.TryRename 统一执行;失败以 ResultCode 回包,不抛异常断连,
/// 框架 RPC ErrorCode 始终保持 0。响应回带最新 Nickname / RenameCount / Diamond 供客户端对账 HUD。
/// </summary>
public sealed class C2G_RenameRequestHandler : MessageRPC<C2G_RenameRequest, G2C_RenameResponse>
{
    protected override async FTask Run(Session session, C2G_RenameRequest request,
        G2C_RenameResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 RenameRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = RenameResultCode.NotLoggedIn;
            response.Nickname = string.Empty;
            response.RenameCount = 0;
            response.Diamond = 0;
            return;
        }

        var (resultCode, nickname, renameCount, diamond) =
            await RenameHelper.TryRename(session.Scene, account, request.NewNickname);

        response.ResultCode = resultCode;
        response.Nickname = nickname;
        response.RenameCount = renameCount;
        response.Diamond = diamond;
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_PropertyChangeRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
