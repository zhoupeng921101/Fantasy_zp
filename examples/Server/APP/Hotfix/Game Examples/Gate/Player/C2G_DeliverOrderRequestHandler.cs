using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 订单交付请求入口(Outer RPC,运行在 Gate Scene,P2 全栈迁移·Phase 1·normal 订单)。
///
/// 玩家身份从会话取(同 C2G_PropertyChangeRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求只携带槽位,**不**接受奖励金额、订单类型(服务端按 OrderCursor 自己定位订单,自己算奖励 — 反作弊红线)。
/// 校验 / 刷新结算 / 发奖 / CAS 置 mask 由 MergeOrderServiceHelper.TryDeliver 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
///
/// 响应里附带 MergeOrderSnapshot,客户端整份覆盖本地视图;无需再发独立 push(避免双发)。
/// </summary>
public sealed class C2G_DeliverOrderRequestHandler : MessageRPC<C2G_DeliverOrderRequest, G2C_DeliverOrderResponse>
{
    protected override async FTask Run(Session session, C2G_DeliverOrderRequest request,
        G2C_DeliverOrderResponse response, Action reply)
    {
        response.Slot = request.Slot;

        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 DeliverOrderRequest 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = DeliverOrderResultCode.NotLoggedIn;
            response.Snapshot = MergeOrderSnapshot.Create(); // 占位空 snapshot
            response.EnergyBalance = -1L; // 哨兵:未裁决,客户端不据此 set
            response.PietyBalance = -1L;
            return;
        }

        var (resultCode, energyReward, pietyReward, energyBalance, pietyBalance, snapshot) =
            await MergeOrderServiceHelper.TryDeliver(session, account, request.Slot);

        response.ResultCode = resultCode;
        response.EnergyReward = energyReward;
        response.PietyReward = pietyReward;
        response.EnergyBalance = energyBalance;
        response.PietyBalance = pietyBalance;
        response.Snapshot = snapshot;
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
