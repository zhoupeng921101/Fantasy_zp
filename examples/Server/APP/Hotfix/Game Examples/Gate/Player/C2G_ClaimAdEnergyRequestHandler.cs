using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 看广告领体力(每日限领)请求入口(Outer RPC,运行在 Gate Scene,体力系统:补广告获取源,桩流程)。
///
/// 玩家身份从会话取(同 C2G_WishForEnergyRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求无载荷(不带账号 / 次数)——每日闸 + 体力发全由服务端按 AdEnergyConfigServer + PlayerDoc 派生(反作弊红线)。
/// 懒每日重置 / 门控 / 检空间 / 发体力(夹软上限)/ 计数 +1 由 ClaimAdEnergyHelper.TryClaim 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终保持 0。
/// 响应回带最新 Energy / AdEnergyUsedToday + AdEnergyDailyLimit 供客户端对账 HUD + 算今日剩余次数。
///
/// 桩流程:本轮无真实广告 SDK,客户端点「看广告」直接发本请求;后续接 SDK 只在客户端播放完成回调后再发,服务端不变。
/// </summary>
public sealed class C2G_ClaimAdEnergyRequestHandler : MessageRPC<C2G_ClaimAdEnergyRequest, G2C_ClaimAdEnergyResponse>
{
    protected override async FTask Run(Session session, C2G_ClaimAdEnergyRequest request,
        G2C_ClaimAdEnergyResponse response, Action reply)
    {
        response.AdEnergyDailyLimit = AdEnergyConfigServer.DailyMax;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 ClaimAdEnergyRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = (int)ClaimAdEnergyResultCode.NotLoggedIn;
            response.Energy = 0;
            response.AdEnergyUsedToday = 0;
            return;
        }

        var (resultCode, energy, adEnergyUsedToday) =
            await ClaimAdEnergyHelper.TryClaim(session.Scene, account);

        response.ResultCode = (int)resultCode;
        response.Energy = energy;
        response.AdEnergyUsedToday = adEnergyUsedToday;
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
