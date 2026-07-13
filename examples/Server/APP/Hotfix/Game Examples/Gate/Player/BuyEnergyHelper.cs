using Fantasy.Async;

namespace Fantasy;

/// <summary>
/// 钻石购买体力(命运能量)服务端权威裁决核心(体力系统 Round D)。
///
/// 单价 / 发放量服务端派生(BuyEnergyConfigServer),客户端请求不带数值——反作弊红线,同改名费(RenameHelper)范式。
/// 处理顺序(原子性:先检空间再扣钻再发体力,失败不留半成品):
///   ① 读服务组件 + 当前体力(ReadEnergyAuthoritative 已结算离线恢复)——服务不可用短路;
///   ② 检体力空间:当前体力 + 发放量 > EnergyUpperBound(存储硬上界)→ OverLimit,不扣钻、不发;
///   ③ 扣钻:ChangeProperty(Diamond, -单价, serverAuthoritative, reason="energy_purchase")。
///      钻不足 → NotEnoughDiamond、不发体力;其它失败 → ServiceUnavailable;
///   ④ 发体力:ChangeProperty(Energy, +发放量, serverAuthoritative, reason="energy_purchase")。
///      已检空间,理论必成;极端并发(体力被其它路径顶高)失败 → 退钻(energy_purchase_refund)+ 回 OverLimit,不留「扣了钻没体力」;
///   ⑤ 双 delta 推送(钻 + 体力),让通用属性视图与购买响应不分叉。
///
/// 反作弊红线:请求只表意图,单价 / 发放量服务端说了算,走 serverAuthoritative 跳过客户端 RPC 路径单笔上限 / 频率闸。
/// 失败一律以 ResultCode 回包,不抛异常断连。
/// 设计基线:.claude/rules/data-authority.md(资源服务端权威、限界信任)。
/// </summary>
public static class BuyEnergyHelper
{
    /// <summary>
    /// 执行购买体力裁定。返回 (resultCode, diamond, energy):
    ///   - Success:diamond = 扣后钻石余额、energy = 发后体力余额;
    ///   - NotEnoughDiamond:diamond = 当前钻石余额、energy = 当前体力(供 toast「需要 X,你有 Y」);
    ///   - OverLimit:energy = 当前体力(已满,买不进)、diamond = 当前钻石(未扣);
    ///   - ServiceUnavailable:回带能读到的当前值(读失败为 0)。
    /// </summary>
    public static async FTask<(BuyEnergyResultCode resultCode, long diamond, long energy)> TryBuy(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is null)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, 0L, 0L);
        }

        var cost = BuyEnergyConfigServer.DiamondCost;
        var grant = BuyEnergyConfigServer.EnergyGrant;

        // ① 读当前体力(已结算离线恢复)。读失败 → 服务不可用。
        var (okEnergy, curEnergy) = await PlayerPropertyServiceHelper.ReadEnergyAuthoritative(scene, accountId);
        if (!okEnergy)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, 0L, curEnergy);
        }

        // ② 检体力空间:加满发放量不得超存储硬上界(避免扣钻后发不进,产生「扣了钻没体力」)。
        if (curEnergy + grant > service.EnergyUpperBound)
        {
            return (BuyEnergyResultCode.OverLimit, 0L, curEnergy);
        }

        // ③ 扣钻(服务端权威)。钻不足 / 服务不可用短路,不发体力。
        var (chargeCode, newDiamond) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Diamond, -cost, "energy_purchase", serverAuthoritative: true);
        if (chargeCode == PropertyChangeResultCode.NotEnough)
        {
            return (BuyEnergyResultCode.NotEnoughDiamond, newDiamond, curEnergy);
        }
        if (chargeCode != PropertyChangeResultCode.Success)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, newDiamond, curEnergy);
        }

        // ④ 发体力(服务端权威)。已检空间,理论必成;极端并发失败 → 退钻,回 OverLimit,不留半成品。
        var (grantCode, newEnergy) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Energy, grant, "energy_purchase", serverAuthoritative: true);
        if (grantCode != PropertyChangeResultCode.Success)
        {
            var (refundCode, refundedDiamond) = await PlayerPropertyServiceHelper.ChangeProperty(
                scene, accountId, PropertyType.Diamond, cost, "energy_purchase_refund", serverAuthoritative: true);
            var diamondAfter = refundCode == PropertyChangeResultCode.Success ? refundedDiamond : newDiamond;
            Log.Warning($"BuyEnergyHelper:发体力失败 account={accountId} grantCode={grantCode},已退钻 refundCode={refundCode};回 OverLimit。");
            PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Diamond, diamondAfter, "energy_purchase_refund");
            return (BuyEnergyResultCode.OverLimit, diamondAfter, curEnergy);
        }

        // ⑤ 双 delta 推送(钻 + 体力),避免通用属性视图与购买响应两条余额分叉。
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Diamond, newDiamond, "energy_purchase");
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Energy, newEnergy, "energy_purchase");
        Log.Info($"购买体力成功 account={accountId} cost={cost} grant={grant} diamond={newDiamond} energy={newEnergy}");
        return (BuyEnergyResultCode.Success, newDiamond, newEnergy);
    }
}
