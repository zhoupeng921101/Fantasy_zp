namespace Fantasy;

/// <summary>
/// 奖励条目(运行态):一条奖励 = 类型 + 目标 id + 数量。发奖统一以「内联奖励条目列表、全部发放」表达,
/// 邮件 / 排行榜结算 / 活动结算 / GM 发信共用此结构:内嵌进 MongoDB 文档存储、经服务端种子内联配置、由 GM 紧凑字符串解析。
///   - RewardType:1=货币(Currency)/ 2=道具(Item),取值口径与 Luban GameConfig.reward.ERewardType 一致。
///   - TargetId:货币指 TbNum 资源 id(经 InventoryServiceHelper.MapNumTypeToProperty 映射服务端 PropertyType);道具指 TbItemDef 道具 id。
///   - Amount:发放数量(货币为增量、道具为个数)。
/// 与 Luban 的 GameConfig.reward.RewardEntry(只读 / ByteBuf 构造,专供从配置表读)区分:后者不能内嵌进 BSON 文档或手动构造,
/// 故运行态发奖链(存储 / 传输 / 种子 / 解析)用本 POCO。
/// </summary>
public sealed class RewardEntryDoc
{
    /// <summary>奖励类型:1=货币 / 2=道具(口径同 GameConfig.reward.ERewardType)。</summary>
    public int RewardType { get; set; }

    /// <summary>目标 id:货币指 TbNum id,道具指 TbItemDef id。</summary>
    public int TargetId { get; set; }

    /// <summary>发放数量。</summary>
    public int Amount { get; set; }
}
