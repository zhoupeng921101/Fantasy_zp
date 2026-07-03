using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 玩家背包批次轨的单条批次(嵌入 PlayerDoc.ItemLots 数组,随玩家文档原生序列化,非独立集合)。
// 批次轨承载「有有效期」的道具:每次获得一条批次,各自独立持有过期时刻,分批领取分批过期。
// 与堆叠轨 PlayerDoc.ItemHoldings(itemId → 数量,无独立状态)并列:堆叠轨合并同种道具为一个总数,
// 批次轨不合并——同种道具不同时间获得的两批过期时刻不同,必须各存一条。
// 消耗按 ExpireMs 从早到晚(FIFO,先出快过期的)。

/// <summary>
/// 背包批次:一次获得的一批同种道具,携带该批独立的过期时刻。
/// 过期时刻为服务端权威时钟(TimeHelper.Now)在获得时固化的绝对 Unix 毫秒(UTC),之后只做比较,不存剩余时长。
/// </summary>
public sealed class ItemLot
{
    /// <summary>批次唯一 id(服务端签发,进程内不重复:nowMs 与自增序拼成)。寻址 / 幂等去重 / 客户端投影对齐用。</summary>
    public string LotId { get; set; } = string.Empty;

    /// <summary>道具 id(= Luban item.TbItemDef 行 id)。</summary>
    public int ItemId { get; set; }

    /// <summary>该批持有数量(非负;FIFO 扣减到 0 的批次从数组移除)。</summary>
    public long Count { get; set; }

    /// <summary>获得时刻(服务端权威 Unix 毫秒 UTC)。</summary>
    public long AcquireMs { get; set; }

    /// <summary>过期绝对时刻(服务端权威 Unix 毫秒 UTC)。now >= 本值即过期。</summary>
    public long ExpireMs { get; set; }

    /// <summary>获得来源标识(如 "drop:stage3" / "mail_claim:456";落 player_item_ledger 供溯源)。</summary>
    public string Source { get; set; } = string.Empty;
}
