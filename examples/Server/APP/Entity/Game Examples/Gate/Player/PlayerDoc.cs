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

    /// <summary>
    /// 玩家身份 ID(账号级稳定唯一标识,服务端权威签发)。
    /// 首登时按客户端上交的 localPlayerId 认领(非空)或服务端新生成(空);此后跨登录恒稳定不变。
    /// 旧文档缺字段时初始化器保底空串,登录处理链遇空会触发签发/认领写回(InitOrLoad 之后由调用方处理)。
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

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

    // ---- P2 新增:四种玩法货币并轨服务端权威(2026-06 全栈迁移)----
    // 与既有 Coin/Diamond/Stamina/Level/Exp 彼此独立、不复用、不映射。
    // 旧文档反序列化时缺字段 → MongoDB.Bson 默认值(数值 = 0、long = 0L)即合理初值。
    // 命名约定:守护者经验用 GuardianExp 与 PlayerDoc.Exp(玩家账号经验)显式区分。

    /// <summary>灵力(玩法软货币;非负,与 Coin 同条件过滤范式落账)。</summary>
    public long SoulPower { get; set; }

    /// <summary>虔诚币(长期主线货币;非负,纯增减计数器)。</summary>
    public long Piety { get; set; }

    /// <summary>守护者累积经验(玩法侧第四种货币,与玩家账号经验 Exp 不复用)。</summary>
    public long GuardianExp { get; set; }

    /// <summary>玩法体力余额(非负;带离线随时间恢复,服务端在查询/变更前按 EnergyLastRecoverMs 懒结算)。</summary>
    public long Energy { get; set; }

    /// <summary>体力上次恢复结算时刻(Unix 毫秒,UTC)。首登 setOnInsert 为 nowMs;每次结算成功后刷新到结算所覆盖的整数倍 tick 末端。</summary>
    public long EnergyLastRecoverMs { get; set; }

    /// <summary>schema 版本(加字段时升 + 缺字段保底)。</summary>
    public int SchemaVersion { get; set; }
}
