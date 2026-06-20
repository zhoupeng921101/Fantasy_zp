using System.Collections.Generic;
using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 活动系统服务端权威组件,挂在 Gate Scene 上(玩家会话所在、其 World 配了 MongoDB)。
/// 持活动配置集合句柄(activity_def)+ 活动进度集合句柄(activity_progress)+ 配置内存缓存(只读裁决用)。
/// 周期幂等的并发原子性由 activity_progress 的 _id 复合唯一键 + 条件更新保证,见 ActivityEvalHelper。
/// 设计基线:design-docs/39-activity-server.md。
/// </summary>
public sealed class ActivityServiceComponent : Entity
{
    /// <summary>活动配置集合(activity_def),_id = activity_id。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<ActivityDefDoc>? Defs;

    /// <summary>活动进度集合(activity_progress),_id = "{account}_{activityId}" 唯一键。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<ActivityProgressDoc>? Progress;

    /// <summary>
    /// 活动配置缓存(activity_id → 配置)。启动时从 activity_def 集合载入,运行时只读裁决用。
    /// 缓存仅服务「配置查询(type / cycle / target / reward / 邮件字段)」;
    /// 进度判定与周期键抢占的权威永远走 MongoDB 原子操作,不读缓存(避免并发判断走偏,同 31/32/33 范式)。
    /// </summary>
    public readonly Dictionary<int, ActivityDefDoc> DefCache = new Dictionary<int, ActivityDefDoc>();
}
