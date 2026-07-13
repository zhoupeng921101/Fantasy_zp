using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 钻石购买体力(命运能量)请求入口(Outer RPC,运行在 Gate Scene,体力系统 Round D)。
///
/// 玩家身份从会话取(同 C2G_RenameRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求不带数值(单价 / 发放量服务端派生);检空间 / 扣钻 / 发体力 / 推送由 BuyEnergyHelper.TryBuy 统一执行,
/// 失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终保持 0。响应回带扣后钻石 + 发后体力供客户端对账 HUD。
/// </summary>
public sealed class C2G_BuyEnergyRequestHandler : MessageRPC<C2G_BuyEnergyRequest, G2C_BuyEnergyResponse>
{
    protected override async FTask Run(Session session, C2G_BuyEnergyRequest request,
        G2C_BuyEnergyResponse response, Action reply)
    {
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 BuyEnergyRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = BuyEnergyResultCode.NotLoggedIn;
            response.Diamond = 0;
            response.Energy = 0;
            return;
        }

        var (resultCode, diamond, energy, buyEnergyUsedToday) = await BuyEnergyHelper.TryBuy(session.Scene, account);
        response.ResultCode = resultCode;
        response.Diamond = diamond;
        response.Energy = energy;
        response.BuyEnergyUsedToday = buyEnergyUsedToday;
        response.BuyEnergyDailyLimit = BuyEnergyConfigServer.DailyLimit;
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
