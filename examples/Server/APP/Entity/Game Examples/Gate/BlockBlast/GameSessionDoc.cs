using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// Block Blast 单局权威态持久文档(原生 MongoDB 文档,非框架 Entity)。
// 集合 block_blast_session;_id = PlayerId(登录签发的账号级权威身份)。
// 局内盘面/分数/发牌器全态是服务端权威态(data-authority:玩家进度,服务端唯一事实源),
// 按 playerId 持久,使玩家重开 App / 重连 / 重登能恢复中断前盘面 + 分数 + 后续发牌逐位接续。
//
// 一玩家同时只持一局(单局无尽):upsert 覆盖式写(_id=playerId,新存盘整体替换旧文档),
// 无 version 冲突裁决——服务端是唯一写者(客户端不上传局内态,只发输入),不存在并发双写同一玩家对局。
//
// 续局接续核心:发牌器全态 = RNG 游标(RngS0/RngS1)+ 5 调度标量 + LastAlgo/LastTier。
// 重建时 new XorShift128PlusRng(s0,s1) 复位游标 + ImportFullState 注入标量,使续局后续发牌与中断前同序。

/// <summary>
/// Block Blast 对局持久文档:每 playerId 一行,记当前在局的完整权威态(盘面 + 分数 + 步号 + 候选 + 发牌器全态)。
/// </summary>
public sealed class GameSessionDoc
{
    /// <summary>玩家身份 id(= 登录签发的 PlayerId),作为 _id 主键(按 playerId 隔离,A 读不到 B 的对局)。</summary>
    [BsonId]
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>本局唯一 id(服务端签发,= 建局时的 GameSession 实体 Id)。续局时沿用,客户端据此对齐 gameId。</summary>
    public long GameId { get; set; }

    /// <summary>本局发牌种子(建局签发,回带客户端供发牌预测;续局沿用)。</summary>
    public long Seed { get; set; }

    /// <summary>权威步号(每次成功落子 +1)。</summary>
    public int Step { get; set; }

    /// <summary>权威分数。</summary>
    public int Score { get; set; }

    /// <summary>权威棋盘 8 行位掩码(与 BinaryBoard.RowBinary 同构,固定 8 项)。</summary>
    public int[] Board { get; set; } = System.Array.Empty<int>();

    /// <summary>当前候选队列 shapeId(落子消耗对应候选置 0)。</summary>
    public int[] CandidateQueue { get; set; } = System.Array.Empty<int>();

    // ── 发牌器全运行态 ──────────────────────────────────────────────

    /// <summary>当前候选批对应的发牌算法序号(整批消耗后 AddWeight 反馈用;-1 = null)。</summary>
    public int LastTrioAlgo { get; set; }

    /// <summary>xorshift128+ 内部状态字 s0(发牌游标),以 long 承载 ulong 位型。</summary>
    public long RngS0 { get; set; }

    /// <summary>xorshift128+ 内部状态字 s1(发牌游标),以 long 承载 ulong 位型。</summary>
    public long RngS1 { get; set; }

    /// <summary>动态权重(跨手累积调度态)。</summary>
    public int DynamicWeight { get; set; }

    /// <summary>上一手权重增量(同向/换向判定)。</summary>
    public int PreDynamicWeight { get; set; }

    /// <summary>本局已发过几次 trio(FirstRound 触发判定)。</summary>
    public int RefillIndex { get; set; }

    /// <summary>清屏窗口是否激活。</summary>
    public bool BcInWindow { get; set; }

    /// <summary>清屏冷却剩余回合。</summary>
    public int BcCooldown { get; set; }

    /// <summary>发牌器 LastAlgo 序号(-1 = null)。</summary>
    public int GenLastAlgo { get; set; }

    /// <summary>发牌器 LastTierId(int.MinValue = null)。</summary>
    public int GenLastTierId { get; set; }

    /// <summary>末次存盘服务端时刻(Unix 毫秒, UTC),供排查 / 审计。</summary>
    public long LastUpdateUnixMs { get; set; }

    /// <summary>
    /// 局内 cosmetic + 合成经济叠加层不透明切片(客户端 MergeIngameSave 的 JSON 原文)。
    /// 承载盘面颜色 / 元素叠加层 / 手牌颜色元素 algo / 合成区库存 / pending 元素预算 / 连消态 / 订单本地统计——
    /// 这些叠在权威 shapeId 序列上、无法从服务端盘面位掩码重推,故随对局文档一并携带。
    /// opaque:服务端只搬运、绝不 Deserialize / 校验其内容;按 playerId 隔离(随 _id 走);随会话文档同生共死
    /// (GameOver 删档时切片一并没,下一局无切片,客户端走缺省重置)。空串 = 无切片。
    /// </summary>
    public string SliceJson { get; set; } = string.Empty;
}
