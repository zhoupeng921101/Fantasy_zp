using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 排行榜服务端存储的原生 MongoDB 文档定义(非框架 Entity)。
// 全服分数集合需要 MongoDB 原子条件操作(取最优条件写)防并发低分覆盖高分,结算幂等标记需要原子条件写防同周期重复结算,
// 框架 IDatabase 高层 API 只能先读后写(设计 31 §五 / 设计 33 §三 明令禁止),故直接用原生 IMongoDatabase + BSON 文档。
// 设计基线:design-docs/31-rank-server.md §二/§五、design-docs/33-rank-settle-server.md §二/§三。

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
/// 榜定义文档:服务端权威的榜级配置副本(入榜要求 / 入榜上限 / 展示上限 + 结算时机 + 结算邮件模板 + 名次奖励档)。
/// 集合 rank_def;_id = 榜 id。与客户端 rank.xlsx 同源口径(同榜 id 两端配置一致, SV12);
/// 结算字段(valid_type/valid_val/mail/名次档 reward)由设计 33 结算编排消费(设计 31 上报/查榜不消费结算字段)。
/// 名次奖励档为同榜 id 多行聚合(rank.xlsx 同 id 多行 = 一榜多档),内嵌为 Tiers 列表(按 RankMin 升序)。
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

    /// <summary>结算时机类型(0 无结算 / 1 开服 X 天 / 2 指定时间 / 3 周循环星期 X)。对应 rank.xlsx valid_type(设计 33 §3.1)。</summary>
    public int ValidType { get; set; }

    /// <summary>结算时机参数(配合 ValidType:1=天数 / 2=指定时间秒 / 3=星期 1..7)。对应 rank.xlsx valid_val。</summary>
    public long ValidVal { get; set; }

    /// <summary>结算邮件模板 id(指向 mail 模板;0 = 榜无结算邮件)。对应 rank.xlsx mail。</summary>
    public int MailDefId { get; set; }

    /// <summary>名次奖励档(同榜 id 多行聚合,按 RankMin 升序)。对应 rank.xlsx 同 id 多行的 rank_min/rank_max/reward。</summary>
    public List<RankRewardTierDoc> Tiers { get; set; } = new List<RankRewardTierDoc>();

    /// <summary>
    /// 点赞奖内联奖励条目(榜级不分档,空列表 = 该榜无点赞奖)。对应 rank.xlsx reward_praise。
    /// 每日领取一次(服务端跨天重置),经 SendMailTo 投点赞奖邮件、领取时全部发放。
    /// </summary>
    public List<RewardEntryDoc> PraiseRewards { get; set; } = new List<RewardEntryDoc>();
}

/// <summary>
/// 名次奖励档(榜定义内嵌项):一个名次区间对应一组内联奖励条目。
/// 对应 rank.xlsx 一行的 rank_min / rank_max / reward;同榜多行 = 多档,聚合进 RankDefDoc.Tiers。
/// 结算时按账号名次落哪档发哪档奖(空列表 = 该档无奖, 设计 33 §3.2)。
/// IgnoreExtraElements 容忍旧 schema 文档(曾带退役的 Reward int 字段)反序列化不崩(退役字段兼容约定)。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class RankRewardTierDoc
{
    /// <summary>名次区间下界(含)。对应 rank.xlsx rank_min。</summary>
    public int RankMin { get; set; }

    /// <summary>名次区间上界(含)。对应 rank.xlsx rank_max。</summary>
    public int RankMax { get; set; }

    /// <summary>该档内联奖励条目(空列表 = 该档无奖),结算时全部发放。对应 rank.xlsx reward。</summary>
    public List<RewardEntryDoc> Rewards { get; set; } = new List<RewardEntryDoc>();

    /// <summary>
    /// 该档每日奖内联奖励条目(空列表 = 该档无每日奖)。对应 rank.xlsx reward_daily(每日按名次档)。
    /// 每日领取一次(服务端跨天重置),经 SendMailTo 投每日奖邮件、领取时全部发放。
    /// </summary>
    public List<RewardEntryDoc> DailyRewards { get; set; } = new List<RewardEntryDoc>();
}

/// <summary>
/// 结算幂等标记文档:某榜「上次结算的应结时刻」(本周期结算时刻作幂等键,设计 33 §3.3)。
/// 集合 rank_settle;_id = 榜 id。
/// 「判本周期未结 + 写本周期已结」用原子条件写(FindOneAndUpdate filter 存量&lt;本周期时刻 + upsert):
/// 周循环本周时刻 &gt; 存量标记 → 可结、结后写本周时刻;一次性结后标记落该次时刻、之后永不再结。
/// 并发两次结算检查只有一次原子成功(执行发奖),另一次判已结跳过(SV6/SV8 不重复发)。持久 MongoDB 跨会话/重启(SV7/SV10)。
/// </summary>
public sealed class RankSettleMarkDoc
{
    /// <summary>榜 id,作为 _id 主键。</summary>
    [BsonId]
    public int RankId { get; set; }

    /// <summary>上次结算的「本周期应结时刻」(服务端 Unix 毫秒, UTC)。下次比较:本周期应结时刻 &gt; 此值 → 可结。</summary>
    public long LastSettledPeriodMs { get; set; }

    /// <summary>实际写入该标记的服务端时刻(Unix 毫秒, UTC),供排查/审计;不参与幂等判定。</summary>
    public long SettledAtMs { get; set; }
}

/// <summary>
/// 每日/点赞领取标记文档:某账号在某榜上次领每日奖 / 点赞奖的「当天 00:00 UTC 时刻」(幂等键,防同日重领)。
/// 集合 rank_claim;_id = "{account}|{rankId}" 复合唯一键(每玩家每榜一条,daily 与 praise 各一字段)。
/// 「判今日未领 + 写今日已领」用原子条件写(FindOneAndUpdate filter 存量 &lt; 今日 00:00 + upsert):今日时刻 &gt; 存量标记 → 可领、领后写今日时刻;
/// 同日再领 filter 不匹配 + upsert 撞 _id 主键(11000)→ 判已领跳过(防重领)。持久 MongoDB 跨会话/重启。
/// IgnoreExtraElements 容忍未来字段增减反序列化不崩(同 RankRewardTierDoc 约定)。
/// </summary>
[BsonIgnoreExtraElements]
public sealed class RankClaimMarkDoc
{
    /// <summary>"{account}|{rankId}" 复合唯一键,作为 _id 主键。</summary>
    [BsonId]
    public string UniqueKey { get; set; } = string.Empty;

    /// <summary>账号(从会话取的设备账号,非客户端自报)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>榜 id。</summary>
    public int RankId { get; set; }

    /// <summary>上次领每日奖的「当天 00:00 UTC 时刻」(服务端 Unix 毫秒;0 = 从未领)。今日 00:00 &gt; 此值 → 今日可领。</summary>
    public long LastDailyClaimDayMs { get; set; }

    /// <summary>上次领点赞奖的「当天 00:00 UTC 时刻」(服务端 Unix 毫秒;0 = 从未领)。今日 00:00 &gt; 此值 → 今日可领。</summary>
    public long LastPraiseClaimDayMs { get; set; }
}
