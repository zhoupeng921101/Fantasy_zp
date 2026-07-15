using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 活动系统服务端持久化层的原生 MongoDB 文档定义(非框架 Entity)。
// 框架 IDatabase 高层 API 只能先读后写(SKILL.md / 32 / 35 / 37 明令禁止),
// 周期幂等键的「判未发 + 写已发」必须单条原子 FindOneAndUpdate(条件过滤 $lt + $set + upsert),
// 故直接走原生 IMongoCollection<T> + BSON 文档。
// 设计基线:design-docs/39-activity-server.md §3.1 / §3.2 / §3.3 / §3.4。

/// <summary>
/// 活动配置文档:服务端权威活动定义(配置表的一行)。
/// 集合 activity_def;_id = activity_id。
/// 与设计 39 §3.1 字段集对应,但 mail_def 不再是「指向 mail_template 的间接 id」,
/// 改为直接内嵌邮件字段(SenderTextId / TitleTextId / ContentTextId / ExpireDays):
///   — 服务端工程无 Luban 集成(同 30/31/32 先例),活动配置以本表为权威源(同 rank_def / mail_template 范式);
///   — 32 SendMailTo 入口签名直接吃散字段(sender/title/content/expireDays/rewardId),
///     不另引「activity 查 mail 模板」中间层(减少一次查表 + 避免循环依赖)。
/// 后续刀加新活动 = 加新行 + 在服务端 ActivityType 分支按需加触发钩子。
/// </summary>
public sealed class ActivityDefDoc
{
    /// <summary>活动 id,作为 _id 主键。对应 activity.xlsx activity_id。</summary>
    [BsonId]
    public int ActivityId { get; set; }

    /// <summary>活动名 textId(占位口径同 num/item/mail/rank,客户端查多语言表显示)。对应 activity.xlsx name_text_id。</summary>
    public int NameTextId { get; set; }

    /// <summary>活动描述 textId。对应 activity.xlsx desc_text_id。</summary>
    public int DescTextId { get; set; }

    /// <summary>
    /// 触发类型(本子单仅接 Login,其余三类留 O3 架构接缝不实做)。对应 activity.xlsx type。
    /// 1=Login(玩家登录时 +1)/ 2=Cumulative(业务系统调进程内 API)/ 3=Schedule(服务端定时器)/ 4=Action(业务系统事件钩子)。
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// 周期(决定本周期键算法 + counter 跨周期清零)。对应 activity.xlsx cycle。
    /// 1=Daily 服务端跨日 0:00 重置 / 2=Weekly 跨周一 0:00 / 3=OneShot 永发一次性 / 4=Always 满足即可领(本子单不取)。
    /// </summary>
    public int Cycle { get; set; }

    /// <summary>达标阈值(counter 需 ≥ target 才达标)。对应 activity.xlsx target。</summary>
    public long Target { get; set; }

    /// <summary>达标发放的内联奖励条目(空列表 = 仅记已发不投邮件),达标时全部发放。对应 activity.xlsx reward。</summary>
    public List<RewardEntryDoc> Rewards { get; set; } = new List<RewardEntryDoc>();

    /// <summary>活动结算邮件发件人 textId(占位口径同 mail.xlsx 110700)。对应 39 §3.1 mail_def 展开。</summary>
    public int SenderTextId { get; set; }

    /// <summary>活动结算邮件标题 textId(多语言)。对应 39 §3.1 mail_def 展开。</summary>
    public int TitleTextId { get; set; }

    /// <summary>活动结算邮件正文 textId。对应 39 §3.1 mail_def 展开。</summary>
    public int ContentTextId { get; set; }

    /// <summary>活动邮件有效期天数(投出后该天数内未领过期;0 = 用全局 retain_days 兜底)。对应 39 §3.1 mail_def 展开。</summary>
    public int ExpireDays { get; set; }

    /// <summary>活动开放时间(unix 毫秒,0 = 永远开放)。对应 activity.xlsx start_at。</summary>
    public long StartAtMs { get; set; }

    /// <summary>活动关闭时间(unix 毫秒,0 = 永不关闭)。对应 activity.xlsx end_at。</summary>
    public long EndAtMs { get; set; }
}

/// <summary>
/// 活动进度文档:每账号每活动一条进度记录。
/// 集合 activity_progress;_id = "{account}_{activityId}" 复合唯一键(MongoDB 主键天然索引,同 31 rank_score / 32 mail_record 范式)。
/// 周期键幂等(设计 39 §3.3):lastClaimedCycleKey 比对「本周期键」(服务端时钟算)
///   — Daily = 今日 0:00 ticks / Weekly = 本周一 0:00 ticks / OneShot = 1 常量;
///   原子 FindOneAndUpdate(filter _id 匹配 且 lastClaimedCycleKey &lt; 本周期键, $set 本周期键, upsert) 单次原子抢占。
/// 抢占成功才发奖(claim-then-act,§3.4)→ 同周期重复 / 并发 / 重启不重发(SV4/SV6/SV7)。
/// </summary>
public sealed class ActivityProgressDoc
{
    /// <summary>"{account}_{activityId}" 复合唯一键,作为 _id 主键。</summary>
    [BsonId]
    public string UniqueKey { get; set; } = string.Empty;

    /// <summary>账号 UUID(沿 35 accounts._id 值)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>活动 id(沿 activity_def.ActivityId)。</summary>
    public int ActivityId { get; set; }

    /// <summary>
    /// 当前周期计数器(Daily 跨天清零、Weekly 跨周清零、OneShot/Always 不清零)。
    /// 本子单 Login 类计数 = 同周期登录次数(只需 +1 即达标 target=1,target&gt;1 类如「连续登录 N 天」留后续刀)。
    /// </summary>
    public long Counter { get; set; }

    /// <summary>
    /// 本活动上次发放的周期键(幂等键,§3.3)。
    /// Daily = 上次发奖那天 0:00 ticks / Weekly = 上次发奖那周一 0:00 ticks / OneShot = 1 表已发 / Always = 0 永发。
    /// 抢占原子写:filter `lastClaimedCycleKey &lt; 本周期键` + $set 本周期键。
    /// </summary>
    public long LastClaimedCycleKey { get; set; }

    /// <summary>末次更新时间(服务端 unix ms,UTC,审计/调试用,不参与幂等判定)。</summary>
    public long LastUpdatedAt { get; set; }

    /// <summary>schema 版本(预留迁移,本子单恒 1)。</summary>
    public int Version { get; set; }
}
