namespace Fantasy;

/// <summary>
/// 看广告领体力(每日限领)服务端权威镜像(体力系统:补广告获取源,桩流程)。
///
/// 单源原则(参 WishConfigServer / BuyEnergyConfigServer 镜像手法):发放量与每日上限是服务端占位常量,
/// 无 Luban 表 id,故服务端固定常量镜像;改数值在此处改。
/// 体力软上限(发体力夹 cap)不在此镜像:复用 MergeOrderConfigServer.EnergyCap(读 Luban global.xlsx id=4),
/// 避免同一软上限两处常量分叉。
///
/// 桩流程:本轮无真实广告 SDK,看广告即直接发本量;后续接 SDK 只换客户端「播放广告完成」那一步,本配置与服务端发放逻辑不变。
/// </summary>
public static class AdEnergyConfigServer
{
    /// <summary>每日看广告领体力次数上限。跨天懒重置后重新计数。</summary>
    public const int DailyMax = 5;

    /// <summary>一次看广告补回的体力。发放夹 EnergyCap 软上限,不溢出。</summary>
    public const int EnergyGrant = 30;
}
