namespace Fantasy;

/// <summary>
/// 祈愿(每日限领体力)服务端权威镜像(原云存档 blob 退役·第 3 批·子批 3a)。
///
/// 单源原则(参 MergeOrderConfigServer / RenameConfigServer 镜像手法):祈愿的三项配置是客户端编译期常量
/// (MergeOrderConfig.WishSoulCost / WishEnergyGain / WishPerDayLimit),无 Luban 表 id,故服务端同样固定常量镜像。
/// 改客户端 MergeOrderConfig 对应常量时须同步改此处。
///
/// 体力软上限(祈愿发体力夹 cap)不在此镜像:复用 MergeOrderConfigServer.EnergyCap(读 Luban global.xlsx id=4,
/// 与客户端 MergeOrderConfig.EnergyCap 同源),避免同一软上限两处常量分叉。
/// </summary>
public static class WishConfigServer
{
    /// <summary>一次祈愿消耗的灵力(= 客户端 MergeOrderConfig.WishSoulCost)。</summary>
    public const int WishSoulCost = 20;

    /// <summary>一次祈愿补回的体力(= 客户端 MergeOrderConfig.WishEnergyGain)。发放夹 EnergyCap 软上限,不溢出。</summary>
    public const int WishEnergyGain = 10;

    /// <summary>每日祈愿次数上限(= 客户端 MergeOrderConfig.WishPerDayLimit)。跨天懒重置后重新计数。</summary>
    public const int WishDailyLimit = 3;
}
