using System.Collections.Concurrent;
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

    /// <summary>结算幂等标记集合(rank_settle),_id = 榜 id。Init 前 / MongoDB 不可达时为 null。原子条件写防同周期重复结算(设计 33 §3.3)。</summary>
    public IMongoCollection<RankSettleMarkDoc>? SettleMarks;

    /// <summary>
    /// 榜定义缓存(榜 id → 榜级配置 + 结算字段 + 名次档)。启动时从 rank_def 集合载入,运行时只读裁决/结算用。
    /// 缓存仅服务「榜存在性 / 入榜要求 / 上限 / 结算时机 / 名次档」这类静态配置查询;
    /// 全服分数与结算幂等的权威永远走 MongoDB 原子操作,不读缓存(避免并发判断走偏)。
    /// </summary>
    public readonly Dictionary<int, RankDefDoc> DefCache = new Dictionary<int, RankDefDoc>();

    /// <summary>结算节律重复定时器 id(进程内调度,设计 33 §3.4);Destroy 时取消。0 = 未起。</summary>
    public long SettleTimerId;

    /// <summary>
    /// 反作弊频率追踪:键 "{account}|{rankId}" → 上次穿过反作弊裁决的提交时刻(Unix 毫秒)。
    /// 进程内字典,不持久化(进程重启清零;攻击者重启服务端的成本远高于刷分收益,本增量按可接受边界)。
    /// 用 ConcurrentDictionary 防 Scene 调度模式变化(当前 Gate 单线程,字典本身也保险)。
    /// </summary>
    public readonly ConcurrentDictionary<string, long> AntiCheatLastSubmitAtMs = new ConcurrentDictionary<string, long>();
}
