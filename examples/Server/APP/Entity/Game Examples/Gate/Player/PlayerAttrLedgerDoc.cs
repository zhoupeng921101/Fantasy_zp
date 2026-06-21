using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 玩家三属性变更审计流水文档(非框架 Entity,原生 MongoDB)。
// 集合 player_attr_ledger,主键 = MongoDB ObjectId 默认自增(_id 单调有序,毫秒同序兜底)。
// 追加式 insert,**永不** update / delete(SV14 + SV18 Code Review 必拦);
// 每笔成功属性变更挂在 37 ChangeProperty 写库成功裁决后写一行,
// 配套查询索引 (Account ASC, Timestamp DESC) 复合 + (Timestamp DESC) 单字段(SV2)。
// 设计基线:design-docs/44-player-attr-ledger.md §3.1。

/// <summary>
/// 玩家三属性变更流水(金币 / 钻石 / 体力 任一笔成功变更 → 一行):
/// 记 (timestamp, account, kind, balanceBefore, balanceAfter, delta, source, reasonRaw, schemaVersion),
/// 供反作弊溯源 / 客服查账 / 玩家自查 / Tier 2+ 退款回滚地基。
/// 字段不变量(写入前 assert,SV10):balanceAfter == balanceBefore + delta + balanceBefore/After >= 0。
/// </summary>
public sealed class PlayerAttrLedgerDoc
{
    /// <summary>MongoDB ObjectId 主键(默认自增,内嵌 Mongo 端 insert 时刻,与 Timestamp 字段允许微秒差,SV12 同毫秒并发兜底)。</summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>应用端写库成功时刻(Unix 毫秒 UTC,= 37 FindOneAndUpdate 成功裁决时刻)。索引字段。</summary>
    public long Timestamp { get; set; }

    /// <summary>账号 id(= UUID,与 accounts._id / players._id 同源)。索引字段。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>属性种类(沿 37 PropertyType 枚举:Coin=0 / Diamond=1 / Stamina=2)。协议层 kind 整数错开一位(1/2/3 对应 Coin/Diamond/Stamina,0 留作 sentinel 表「不过滤」,见 AttrLedgerQueryHelper.cs:32 解释)。</summary>
    public PropertyType Kind { get; set; }

    /// <summary>变更前余额(非负;= 37 FindOneAndUpdate 后余额 - delta,等价 returnDocument Before)。</summary>
    public long BalanceBefore { get; set; }

    /// <summary>变更后余额(非负;= 37 FindOneAndUpdate 返回的新余额,推送 G2C_PropertyDeltaPush 同源)。</summary>
    public long BalanceAfter { get; set; }

    /// <summary>该笔变更的相对量(有符号,正 = 增加 / 负 = 减少;BalanceAfter = BalanceBefore + Delta)。</summary>
    public long Delta { get; set; }

    /// <summary>变更来源枚举(单一映射函数从 ReasonRaw 推导,SV3,见 AttrLedgerHelper.MapReasonToSource)。</summary>
    public AttrChangeSource Source { get; set; }

    /// <summary>调用方原始 reason 字符串(保留原文,业务侧子分类如 mailId / codeId / rankIdx 供运营 ad-hoc 查)。</summary>
    public string ReasonRaw { get; set; } = string.Empty;

    /// <summary>schema 版本(本子单 = 1;Tier 2+ 加字段时升 2 + 缺字段保底)。</summary>
    public int SchemaVersion { get; set; }
}
