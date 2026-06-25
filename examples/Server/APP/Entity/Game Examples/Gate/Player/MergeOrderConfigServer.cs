namespace Fantasy;

/// <summary>
/// 订单系统服务端权威常量 + 订单池(P2 全栈迁移·Phase 1·normal 订单)。
///
/// **手抄自客户端,改一处需同步另一处**(Fantasy 无 Luban,服务端配置作为权威源单独存在):
///   - 订单池 8 条     ← `Assets/GameScripts/HotFix/GameLogic/Module/BlockBlast/MergeOrderConfig.cs` `OrderPool` 数组
///   - MergeCount=4    ← 同上 `MergeCount`(合成比,难度公式 d = Count × MergeCount^(Level-1) 用)
///   - PietyPerDifficulty=30 ← `TempleConfig.cs` 同名常量(虔诚币奖励系数)
///   - ActiveOrders=3、OrderRefreshIntervalSec=300 ← `GlobalConfigMgr` 默认值(global.xlsx id=1/id=2,本仓库无表,值为客户端代码内默认)
///   - OrderRewardEnergy=8 ← `MergeOrderConfig.OrderRewardEnergy`
///
/// 订单类型(Type)的 int 值必须与客户端 `MergeElement` 枚举一致:None=0/Butterfly=1/Chalice=2/Scroll=3/Star=4;
/// 改 OrderPool 顺序 / 元素类型 / 数量 / 等级,务必同步客户端同名数组。
///
/// 设计基线:.claude/rules/data-authority.md(玩家持久数据服务端权威);客户端段保持显示逻辑兼容,
/// 但奖励 / 订单进度 / 刷新节律以服务端为权威。
/// </summary>
public static class MergeOrderConfigServer
{
    /// <summary>订单类型枚举值(= 客户端 MergeElement)。0 = None(空槽占位)。</summary>
    public const int OrderTypeNone = 0;
    public const int OrderTypeButterfly = 1;
    public const int OrderTypeChalice = 2;
    public const int OrderTypeScroll = 3;
    public const int OrderTypeStar = 4;

    /// <summary>同时激活的订单槽数(= 客户端 GlobalConfigMgr.OrderCountValue 默认 3)。</summary>
    public const int ActiveOrders = 3;

    /// <summary>订单按时整批刷新间隔秒数(= 客户端 GlobalConfigMgr.OrderRefreshSecondsValue 默认 300)。</summary>
    public const int OrderRefreshIntervalSec = 300;

    /// <summary>订单按时整批刷新间隔毫秒(服务端时钟用,= IntervalSec * 1000)。</summary>
    public const long OrderRefreshIntervalMs = OrderRefreshIntervalSec * 1000L;

    /// <summary>单次交付奖励体力(= 客户端 MergeOrderConfig.OrderRewardEnergy)。</summary>
    public const int OrderRewardEnergy = 8;

    /// <summary>合成比(难度计算用):d = Count × MergeCount^(Level-1)。= 客户端 MergeOrderConfig.MergeCount。</summary>
    public const int MergeCount = 4;

    /// <summary>虔诚币奖励系数(= 客户端 TempleConfig.PietyPerDifficulty)。订单虔诚币 = 难度 × 此值。</summary>
    public const int PietyPerDifficulty = 30;

    /// <summary>单条订单:类型 / 等级 / 数量。</summary>
    public readonly struct OrderEntry
    {
        public readonly int Type;
        public readonly int Level;
        public readonly int Count;
        public OrderEntry(int type, int level, int count) { Type = type; Level = level; Count = count; }

        /// <summary>难度量 d = Count × MergeCount^(Level-1)(= 客户端 Order.Difficulty)。</summary>
        public int Difficulty
        {
            get
            {
                int r = 1;
                for (int i = 0; i < Level - 1; i++) r *= MergeCount;
                return Count * r;
            }
        }
    }

    /// <summary>
    /// 循环订单池(8 条,手抄自客户端 OrderPool)。游标 cursor 按 length 取模循环。
    /// 难度量(d=Count×4^(Level-1)):1/4/16/1/512/1/4/16,锯齿波节奏。
    /// </summary>
    public static readonly OrderEntry[] OrderPool =
    {
        new OrderEntry(OrderTypeButterfly, 1, 1), // d=1   易    [初始槽 0]
        new OrderEntry(OrderTypeChalice,   2, 1), // d=4   易    [初始槽 1]
        new OrderEntry(OrderTypeScroll,    3, 1), // d=16  中    [初始槽 2]
        new OrderEntry(OrderTypeStar,      1, 1), // d=1   易
        new OrderEntry(OrderTypeButterfly, 5, 2), // d=512 难
        new OrderEntry(OrderTypeChalice,   1, 1), // d=1   易
        new OrderEntry(OrderTypeScroll,    2, 1), // d=4   易-中
        new OrderEntry(OrderTypeStar,      3, 1), // d=16  中
    };

    /// <summary>
    /// 派生「当前激活订单数组」:由 cursor + deliveredMask 派生。
    /// cursor 语义 = 当前批起始索引(=已刷过的整批数 × ActiveOrders;首登 cursor=0)。
    /// 第 i 槽订单 = pool[(cursor + i) mod length]。已交付的槽(mask bit i 置位)以 Type=0 空槽占位。
    /// 首登 cursor=0:激活订单 = pool[0..ActiveOrders),与客户端 Reset 后 NextOrder() 填三槽得到 pool[0..2] 完全对齐。
    /// 刷新一批 cursor += ActiveOrders,自然推进到 pool[3..5]、pool[6,7,0]、pool[1..3] 等循环。
    /// </summary>
    public static OrderEntry GetActiveOrder(int cursor, int slot)
    {
        int length = OrderPool.Length;
        int baseIdx = cursor + slot;
        // C# % 对负数返回负数;cursor 累加只增不减、slot ≥ 0,理论非负。保留 ((x%length)+length)%length 防御。
        int idx = ((baseIdx % length) + length) % length;
        return OrderPool[idx];
    }
}
