using System.Collections.Generic;
using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 兑换码服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持有三张原生 MongoDB 集合句柄(码表 / 防重记录 / 全局计数)与码表内存缓存,供裁决 Handler 使用。
/// 裁决的并发原子性由 MongoDB 集合的唯一索引 + 条件更新保证,见 RedeemServiceComponentSystem。
/// 设计基线:design-docs/30-redeem-code-server.md。
/// </summary>
public sealed class RedeemServiceComponent : Entity
{
    /// <summary>权威码表集合(redeem_code)。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<RedeemCodeDoc>? CodeTable;

    /// <summary>按账号防重记录集合(redeem_record),_id 为 (account,code) 唯一键。Init 前为 null。</summary>
    public IMongoCollection<RedeemRecordDoc>? Records;

    /// <summary>按码全局计数集合(redeem_counter)。Init 前为 null。</summary>
    public IMongoCollection<RedeemCounterDoc>? Counters;

    /// <summary>
    /// 码表内存缓存(规整码 → 配置)。启动时从 CodeTable 载入,运行时只读裁决用。
    /// 缓存仅服务「码存在性 / 奖励 / 有效期 / 上限」这类静态配置查询;
    /// 防重与计数的权威永远走 MongoDB 原子操作,不读缓存(避免并发判断走偏)。
    /// </summary>
    public readonly Dictionary<string, RedeemCodeDoc> CodeCache = new Dictionary<string, RedeemCodeDoc>();
}
