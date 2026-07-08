using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 玩家道具持有变更审计流水文档(非框架 Entity,原生 MongoDB)。
// 集合 player_item_ledger,主键 = MongoDB ObjectId 默认自增。
// 追加式 insert,永不 update / delete(沿 player_attr_ledger 同规约);
// 每笔成功道具变更(订单交付发碎片 / 塔罗合成扣碎片)写一行,
// 配套查询索引 (Account ASC, Timestamp DESC) 复合 + (Timestamp DESC) 单字段。
// 与 player_attr_ledger 分集合:属性流水按 PropertyType 枚举建模,道具流水按 itemId 建模,维度不同不混表。

/// <summary>
/// 玩家道具持有变更流水(任一笔成功道具增减 → 一行):
/// 记 (timestamp, account, itemId, delta, balanceAfter, reasonRaw, schemaVersion),
/// 供反作弊溯源 / 客服查账。BalanceAfter = 变更后该 itemId 权威持有量。
/// </summary>
public sealed class PlayerItemLedgerDoc
{
    /// <summary>MongoDB ObjectId 主键(默认自增,同毫秒并发以 _id 单调有序兜底)。</summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>应用端写库成功时刻(Unix 毫秒 UTC)。索引字段。</summary>
    public long Timestamp { get; set; }

    /// <summary>账号 id(= UUID,与 accounts._id / players._id 同源)。索引字段。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>道具 id(= Luban item.TbItemDef 行 id)。</summary>
    public int ItemId { get; set; }

    /// <summary>该笔变更的相对量(有符号,正 = 获得 / 负 = 消耗)。</summary>
    public long Delta { get; set; }

    /// <summary>变更后该道具权威持有量(= FindOneAndUpdate 返回的新值)。</summary>
    public long BalanceAfter { get; set; }

    /// <summary>调用方原始 reason 字符串(如 "merge_order_deliver:slot0:..." / "inventory_use:item31001")。</summary>
    public string ReasonRaw { get; set; } = string.Empty;

    /// <summary>schema 版本(初始 1;加字段时升 + 缺字段保底)。</summary>
    public int SchemaVersion { get; set; }
}
