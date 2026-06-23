using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 玩家属性账本的原生 MongoDB 文档定义(非框架 Entity)。
// 服务端权威三属性(金币 / 钻石 / 体力)持久存储,主键 = UUID,与 accounts._id 1:1 同源。
// 变更必须用 MongoDB 单条原子 FindOneAndUpdate(条件过滤 + $inc + $set),防并发同账号双扣超发(SV11)。
// 框架 IDatabase 高层 API 只能先读后写(设计明令禁止),故直接用原生 IMongoCollection<PlayerDoc>。
// 设计基线:design-docs/37-player-attr-server.md §3.1。

/// <summary>
/// 玩家属性账本文档:每 UUID 一行,记三属性余额 + 末次变更时间 + schema 版本。
/// 集合 players;_id = AccountId(与 accounts._id 同值,1:1 关联,SV3)。
/// 主键 MongoDB 天然唯一,无额外索引(本子单无按余额查询需求,Tier 1+ 真要做「金币 >= N 的玩家」类运营查询时再加,SV2)。
/// 首登 setOnInsert 写初始值(金币 0 / 钻石 0 / 体力 5 默认,以 PlayerPropertyServiceComponent 配置为准);
/// 重登走 update 路径但 update 命令完全不触碰余额字段,余额稳定 = 上次变更后的值(SV5)。
/// </summary>
public sealed class PlayerDoc
{
    /// <summary>账号 id(= UUID 字符串,与 accounts._id 同源,SV3),作为 _id 主键。</summary>
    [BsonId]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>金币余额(非负;原子 FindOneAndUpdate 用条件过滤保证 0 <= 余额 + delta <= 上界,SV8/SV9/SV11)。</summary>
    public long Coin { get; set; }

    /// <summary>钻石余额(非负;同上)。</summary>
    public long Diamond { get; set; }

    /// <summary>体力余额(非负;同上)。</summary>
    public long Stamina { get; set; }

    /// <summary>昵称(首登 setOnInsert 默认空串;旧文档缺字段时初始化器保底为空串)。</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>等级(首登 setOnInsert 默认 1;旧文档缺字段时初始化器保底为 1)。</summary>
    public int Level { get; set; } = 1;

    /// <summary>经验(首登 setOnInsert 默认 0;旧文档缺字段保底 0)。</summary>
    public long Exp { get; set; }

    /// <summary>末次属性变更时间(服务端 Unix 毫秒,UTC)。每次 $inc 成功后 $set 刷新;Tier 2+ ledger 落地前作单字段快照。</summary>
    public long LastChangeUnixMs { get; set; }

    /// <summary>schema 版本(加字段时升 + 缺字段保底)。</summary>
    public int SchemaVersion { get; set; }
}
