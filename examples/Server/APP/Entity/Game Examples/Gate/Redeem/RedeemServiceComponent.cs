using System.Collections.Generic;
using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 兑换码服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持有防重记录 / 全局计数两张原生 MongoDB 集合句柄,与码表内存缓存,供裁决 Handler 使用。
/// 码表(码 → 奖励盒 / 有效期 / 上限)是服务端专用 Luban 配置(TbRedeemCode,仅 server 组不进客户端包),
/// 启动时载入缓存;防重与计数是玩家运行态,走 MongoDB 原子操作(唯一索引 + 条件更新),见 RedeemServiceComponentSystem。
/// </summary>
public sealed class RedeemServiceComponent : Entity
{
    /// <summary>按账号防重记录集合(redeem_record),_id 为 (account,code) 唯一键。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<RedeemRecordDoc>? Records;

    /// <summary>按码全局计数集合(redeem_counter)。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<RedeemCounterDoc>? Counters;

    /// <summary>
    /// 码表内存缓存(规整码 → 配置)。启动时从 Luban 服务端码表 TbRedeemCode 载入,运行时只读裁决用。
    /// 缓存仅服务「码存在性 / 奖励 / 有效期 / 上限」这类静态配置查询;
    /// 防重与计数的权威永远走 MongoDB 原子操作,不读缓存(避免并发判断走偏)。
    /// </summary>
    public readonly Dictionary<string, GameConfig.RedeemCode> CodeCache = new Dictionary<string, GameConfig.RedeemCode>();
}
