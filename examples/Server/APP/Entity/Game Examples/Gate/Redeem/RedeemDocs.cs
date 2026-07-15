using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 兑换码服务端存储的原生 MongoDB 文档定义(非框架 Entity)。
// 这三张集合需要 MongoDB 原子条件操作(唯一键插入 / 条件递增)防并发双发与超发,
// 框架 IDatabase 高层 API 只能先读后写(设计 30 §五 明令禁止),故直接用原生 IMongoDatabase + BSON 文档。
// 设计基线:design-docs/30-redeem-code-server.md §二/§五。

/// <summary>
/// 权威码表文档:运营配置的「码 → 奖励盒 / 有效期 / 全局上限」。
/// 集合 redeem_code;_id = 规整后(trim+大写)的码字符串本身(使码成为主键,天然防重复配置)。
/// 发奖走服务端权威奖励盒:兑换成功按 RewardBoxId 调 RewardBoxServiceHelper.GrantBoxAsync 整盒发放(货币落账 + 推送 / 道具入背包),
/// 与全局固定奖励发放同源(不再由客户端本地发奖)。IgnoreExtraElements 容忍旧 schema 文档(曾内联 Rewards 数组)反序列化不崩,
/// 但旧文档 RewardBoxId 缺省为 0(空盒不发)——切换后须清 redeem_code 集合重播种,或由运营改配为盒 id。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class RedeemCodeDoc
{
    /// <summary>规整后(trim+大写)的兑换码,作为 _id 主键。</summary>
    [BsonId]
    public string Code { get; set; } = string.Empty;

    /// <summary>该码发放的奖励盒 id(指向 TbRewardBox)。0 = 无奖励盒(空盒,不发)。</summary>
    public int RewardBoxId { get; set; }

    /// <summary>过期时间(Unix 毫秒, UTC)。0 = 永不过期。</summary>
    public long ExpireUnixMs { get; set; }

    /// <summary>全局发放上限。0 = 不限量。</summary>
    public int GlobalLimit { get; set; }
}

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
