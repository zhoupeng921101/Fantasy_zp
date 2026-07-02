using System;
using System.Collections.Generic;
using GameConfig;

namespace Fantasy;

/// <summary>
/// 订单系统服务端配置访问(单源原则,参 .claude/rules/data-authority.md):
///   - 零散参数 → Luban global.xlsx(`GlobalCfg.GetInt`):
///     · ActiveOrders ← id=1 OrderCount(默认 3)
///     · OrderRefreshIntervalSec ← id=2 OrderRefreshSeconds(默认 300)
///     · ClearToolCost ← id=5 / EnergyCap ← id=4
///   - 订单池与奖励 → Luban block.TbMergeOrder(池序 = id 升序,每行:元素/等级/数量 +
///     体力奖励 / 虔诚币奖励 / 塔罗碎片道具 id / 碎片数量,双端同源,服务端按表自算发奖)。
///
/// 订单元素类型(ElementType)的 int 值与客户端 `MergeElement` 枚举一致:
/// None=0(空槽占位)/ Butterfly=1 / Chalice=2 / Scroll=3 / Star=4,映射由表数据承载。
/// </summary>
public static class MergeOrderConfigServer
{
    /// <summary>空槽占位的订单类型值(= 客户端 MergeElement.None)。</summary>
    public const int OrderTypeNone = 0;

    /// <summary>
    /// 同时激活的订单槽数。读 Luban global.xlsx id=1,缺表/缺键回退默认 3(与客户端 GlobalConfigMgr.OrderCountValue 一致)。
    /// </summary>
    public static readonly int ActiveOrders = GlobalCfg.GetInt(GlobalCfg.OrderCount, 3);

    /// <summary>
    /// 订单按时整批刷新间隔秒数。读 Luban global.xlsx id=2,缺表/缺键回退默认 300(同客户端)。
    /// </summary>
    public static readonly int OrderRefreshIntervalSec = GlobalCfg.GetInt(GlobalCfg.OrderRefreshSeconds, 300);

    /// <summary>订单按时整批刷新间隔毫秒(服务端时钟用,= IntervalSec * 1000)。</summary>
    public static readonly long OrderRefreshIntervalMs = OrderRefreshIntervalSec * 1000L;

    /// <summary>
    /// 每次落子消耗体力(= 客户端 MergeOrderConfig.PlaceCost)。客户端为编译期常量、无 global 表 id,故服务端同样固定常量。
    /// 落子体力服务端派生:成功落子扣 PlaceCost,消行按行列数返还(夹 EnergyCap 软上限),客户端不再自报此往返。
    /// </summary>
    public const int PlaceCost = 1;

    /// <summary>
    /// 消除道具(清一行一列脱困道具)代价体力。读 Luban global.xlsx id=5,缺表/缺键回退默认 5
    /// (= 客户端 MergeOrderConfig.ClearToolCost / GlobalConfigMgr.ClearToolEnergyCostValue)。无返还。
    /// </summary>
    public static readonly int ClearToolCost = GlobalCfg.GetInt(GlobalCfg.ClearToolEnergyCost, 5);

    /// <summary>
    /// 体力被动恢复软上限(= 客户端 MergeOrderConfig.EnergyCap / GlobalConfigMgr.EnergyRecoverCapValue)。
    /// 读 Luban global.xlsx id=4,缺表/缺键回退默认 30。消除道具扣费走服务端权威路径,不受此软上限影响
    /// (软上限只钳被动恢复,主动扣费直接落 delta);此常量供配置一致性核对与潜在校验用。
    /// </summary>
    public static readonly int EnergyCap = GlobalCfg.GetInt(GlobalCfg.EnergyRecoverCap, 30);

    /// <summary>
    /// 循环订单池(Luban block.TbMergeOrder,池序 = id 升序)。游标按池长取模循环。
    /// 表缺失 / 空表 → 空数组:订单派生走空槽、交付返 ServiceUnavailable(可见降级,不静默回退旧值)。
    /// </summary>
    public static readonly MergeOrder[] OrderPool = BuildOrderPool();

    private static MergeOrder[] BuildOrderPool()
    {
        var tb = GameConfigSystem.Tables?.TbMergeOrder;
        if (tb == null || tb.DataList.Count == 0)
        {
            // 静态字段初始化可能早于 Fantasy.Log 就绪,用 Console 输出(同 GameConfigSystem.Load 口径)。
            Console.Error.WriteLine("[MergeOrderConfigServer] block.TbMergeOrder 缺失或为空,订单池不可用:快照将只给空槽,交付返 ServiceUnavailable。请检查 GameConfigBytes 与导表。");
            return Array.Empty<MergeOrder>();
        }
        var list = new List<MergeOrder>(tb.DataList);
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list.ToArray();
    }

    /// <summary>
    /// 派生「当前激活订单」表行:由 cursor + slot 定位池内行。
    /// cursor 语义 = 当前批起始索引(=已刷过的整批数 × ActiveOrders;首登 cursor=0)。
    /// 第 i 槽订单 = pool[(cursor + i) mod length]。池不可用返 null(调用方按服务不可用降级)。
    /// </summary>
    public static MergeOrder? GetActiveOrder(int cursor, int slot)
    {
        var pool = OrderPool;
        int length = pool.Length;
        if (length == 0) return null;
        int baseIdx = cursor + slot;
        // C# % 对负数返回负数;cursor 累加只增不减、slot ≥ 0,理论非负。保留 ((x%length)+length)%length 防御。
        int idx = ((baseIdx % length) + length) % length;
        return pool[idx];
    }
}
