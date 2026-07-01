using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 祈愿(每日限领体力)请求入口(Outer RPC,运行在 Gate Scene,原云存档 blob 退役·第 3 批·子批 3a)。
///
/// 玩家身份从会话取(同 C2G_RenameRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求无载荷(不带账号 / 费用 / 次数)——每日闸 + 灵力费 + 体力发全由服务端按 WishConfigServer + PlayerDoc 派生(反作弊红线)。
/// 懒每日重置 / 门控 / 扣灵力 / 发体力(夹软上限)/ 计数 +1 由 WishHelper.TryWish 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终保持 0。
/// 响应回带最新 SoulPower / Energy / WishUsedToday + WishDailyLimit 供客户端对账 HUD + 算今日剩余次数。
/// </summary>
public sealed class C2G_WishForEnergyRequestHandler : MessageRPC<C2G_WishForEnergyRequest, G2C_WishForEnergyResponse>
{
    protected override async FTask Run(Session session, C2G_WishForEnergyRequest request,
        G2C_WishForEnergyResponse response, Action reply)
    {
        response.WishDailyLimit = WishConfigServer.WishDailyLimit;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 WishForEnergyRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)WishForEnergyResultCode.NotLoggedIn;
            response.SoulPower = 0;
            response.Energy = 0;
            response.WishUsedToday = 0;
            return;
        }

        var (resultCode, soulPower, energy, wishUsedToday) =
            await WishHelper.TryWish(session.Scene, account);

        response.ResultCode = (int)resultCode;
        response.SoulPower = soulPower;
        response.Energy = energy;
        response.WishUsedToday = wishUsedToday;
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
