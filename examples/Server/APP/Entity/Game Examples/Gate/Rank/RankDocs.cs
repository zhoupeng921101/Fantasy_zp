using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 排行榜服务端存储的原生 MongoDB 文档定义(非框架 Entity)。
// 全服分数集合需要 MongoDB 原子条件操作(取最优条件写)防并发低分覆盖高分,
// 框架 IDatabase 高层 API 只能先读后写(设计 31 §二/§五 明令禁止),故直接用原生 IMongoDatabase + BSON 文档。
// 设计基线:design-docs/31-rank-server.md §二/§五。

/// <summary>
/// 全服分数文档:某账号在某榜的一条最佳成绩 + 首次达到该最佳的时间。
/// 集合 rank_score;_id = "{account}|{rankId}" 复合唯一键(使取最优成为单次原子条件写)。
/// 取最优:FindOneAndUpdate(filter _id 匹配 且 BestScore &lt; 新分, $set 新分+时间, upsert)单次原子,
/// 仅严格更高分才刷新,并发下低分写不进(设计 31 §五,SV7)。
/// </summary>
public sealed class RankScoreDoc
{
    /// <summary>"{account}|{rankId}" 复合唯一键,作为 _id 主键。</summary>
    [BsonId]
    public string UniqueKey { get; set; } = string.Empty;

    /// <summary>账号(从会话取的设备账号,非客户端自报)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>榜 id。</summary>
    public int RankId { get; set; }

    /// <summary>该账号该榜的最佳成绩。</summary>
    public long BestScore { get; set; }

    /// <summary>
    /// 首次达到该最佳分的服务端时刻(Unix 毫秒, UTC),作并列排序键(同分早者靠前, SV4)。
    /// 仅当成绩严格更高时随分一起更新;同分重报不更新(保住先到先得,设计 31 §3.3)。
    /// </summary>
    public long AchievedUnixMs { get; set; }
}

/// <summary>
/// 榜定义文档:服务端权威的「入榜要求 / 入榜上限 / 展示上限」榜级配置副本。
/// 集合 rank_def;_id = 榜 id。本增量与客户端 rank.xlsx 同源口径(同榜 id 两端入榜要求/上限一致, SV12);
/// 不消费奖励 / 结算字段(reward / valid_type / mail 等,那是结算的事,不在本增量,设计 31 读前必看 第 4 条)。
/// </summary>
public sealed class RankDefDoc
{
    /// <summary>榜 id,作为 _id 主键。</summary>
    [BsonId]
    public int RankId { get; set; }

    /// <summary>入榜要求(最低入榜分;成绩 &lt; 此值不进榜)。对应 rank.xlsx rank_condition。</summary>
    public long EnterCondition { get; set; }

    /// <summary>入榜上限(参与排名的名额上限;&lt;=0 视作不限)。对应 rank.xlsx rank_count_max。</summary>
    public int RankCountMax { get; set; }

    /// <summary>展示上限(查榜返回条数上限;&lt;=0 视作不限)。对应 rank.xlsx show_count_max。</summary>
    public int ShowCountMax { get; set; }
}
