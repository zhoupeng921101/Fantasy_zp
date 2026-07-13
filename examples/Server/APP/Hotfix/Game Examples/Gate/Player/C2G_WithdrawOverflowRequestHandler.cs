using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 取用体力溢出储存请求入口(Outer RPC,运行在 Gate Scene,体力系统 Round G)。
///
/// 玩家身份从会话取(同 C2G_BuyEnergyRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求不带数量(取用量服务端派生 = 整池尽量转入体力,受硬顶约束);结算 / 转账 / 推送由 WithdrawOverflowHelper.TryWithdraw 统一执行,
/// 失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终保持 0。响应回带取用后体力 + 池余 + 本次取用量供客户端对账。
/// </summary>
public sealed class C2G_WithdrawOverflowRequestHandler : MessageRPC<C2G_WithdrawOverflowRequest, G2C_WithdrawOverflowResponse>
{
    protected override async FTask Run(Session session, C2G_WithdrawOverflowRequest request,
        G2C_WithdrawOverflowResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 WithdrawOverflowRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = WithdrawOverflowResultCode.NotLoggedIn;
            response.Energy = 0;
            response.EnergyOverflowPool = 0;
            response.Withdrawn = 0;
            return;
        }

        var (resultCode, energy, pool, withdrawn) = await WithdrawOverflowHelper.TryWithdraw(session.Scene, account);
        response.ResultCode = resultCode;
        response.Energy = energy;
        response.EnergyOverflowPool = pool;
        response.Withdrawn = withdrawn;
    }

    /// <summary>从会话取登录时绑定的账号名(同 C2G_BuyEnergyRequestHandler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.Name;
    }
}
