using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 兑换码服务端运行态存储的原生 MongoDB 文档定义(非框架 Entity)。
// 这两张集合需要 MongoDB 原子条件操作(唯一键插入 / 条件递增)防并发双发与超发,
// 框架 IDatabase 高层 API 只能先读后写,故直接用原生 IMongoDatabase + BSON 文档。
// 码表(码 → 奖励盒 / 有效期 / 上限)已移到服务端专用 Luban 配置(TbRedeemCode),不在此存储。

/// <summary>
/// 按账号防重记录:每条 = 某账号已兑过某码。
/// 集合 redeem_record;_id = "{account}|{code}" 复合唯一键。
/// 第二次插入同 _id 抛 DuplicateKey → 裁定「已兑过」(原子条件写, SV7)。
/// </summary>
public sealed class RedeemRecordDoc
{
    /// <summary>"{account}|{code}" 复合唯一键,作为 _id 主键。</summary>
    [BsonId]
    public string UniqueKey { get; set; } = string.Empty;

    /// <summary>账号(从会话取的设备账号,非客户端自报)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>规整后的码。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>兑换成功的服务端时刻(Unix 毫秒, UTC)。</summary>
    public long RedeemedUnixMs { get; set; }
}

/// <summary>
/// 按码全局计数:每条 = 某码已被成功兑换的总次数。
/// 集合 redeem_counter;_id = 规整后的码字符串。
/// 条件递增(filter: Count &lt; Limit, $inc Count) → 达上限即不再递增(原子计数, SV8)。
/// </summary>
public sealed class RedeemCounterDoc
{
    /// <summary>规整后的码,作为 _id 主键。</summary>
    [BsonId]
    public string Code { get; set; } = string.Empty;

    /// <summary>已成功兑换次数。</summary>
    public int Count { get; set; }
}
