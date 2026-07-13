namespace Fantasy;

/// <summary>
/// 钻石购买体力(命运能量)服务端配置(体力系统 Round D)。
/// 单价 / 发放量服务端派生,客户端请求不上报——反作弊红线。数值为占位常量(取自策划补充体力弹窗 mockup:10 钻 → 100 体力),
/// 与客户端展示常量手抄同步(沿 EnergyInitial / EnergyStart 同款「手抄惯例」);后续可上 Luban 两端同源消除重复。
/// </summary>
public static class BuyEnergyConfigServer
{
    /// <summary>单次购买消耗钻石数(占位 10)。</summary>
    public const long DiamondCost = 10L;

    /// <summary>单次购买发放体力数(占位 100)。</summary>
    public const long EnergyGrant = 100L;
}
