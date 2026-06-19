using System.Collections.Generic;
using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 排行榜服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持有全服分数集合句柄(rank_score)与榜定义内存缓存(榜级入榜/上限配置),供上报/查榜 Handler 使用。
/// 取最优的并发原子性由 rank_score 的 _id 复合唯一键 + 条件更新保证,见 RankServiceComponentSystem / RankDecisionHelper。
/// 设计基线:design-docs/31-rank-server.md。
/// </summary>
public sealed class RankServiceComponent : Entity
{
    /// <summary>全服分数集合(rank_score),_id 为 "{account}|{rankId}" 唯一键。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<RankScoreDoc>? Scores;

    /// <summary>
    /// 榜定义缓存(榜 id → 入榜要求/上限)。启动时从 rank_def 集合载入,运行时只读裁决用。
    /// 缓存仅服务「榜存在性 / 入榜要求 / 入榜上限 / 展示上限」这类静态配置查询;
    /// 全服分数的权威永远走 MongoDB 原子操作,不读缓存(避免并发判断走偏)。
    /// </summary>
    public readonly Dictionary<int, RankDefDoc> DefCache = new Dictionary<int, RankDefDoc>();
}
