using GameLogic;

namespace Fantasy;

/// <summary>
/// 发牌器全运行态的只读快照,供 Hotfix 侧组装协议 BlockGenState 与持久化 GameSessionDoc。
/// 置于 Entity 程序集:DynamicWeightDiff 的全态导出经 <see cref="DynamicWeightDiff.ExportFullState"/>
/// (返回 internal 可见的 FullState 结构),Hotfix 是独立程序集,故跨手累积态 + PRNG 游标的取值在此处完成,
/// Hotfix 只消费本结构(已把 ulong 游标转成 long 位型,便于协议 int64 / Bson 落盘)。
/// </summary>
public readonly struct GameSessionGenStateView
{
    public readonly int DynamicWeight;
    public readonly int PreDynamicWeight;
    public readonly int RefillIndex;
    public readonly bool BcInWindow;
    public readonly int BcCooldown;

    /// <summary>xorshift128+ 内部状态字 s0(发牌游标),以 long 承载 ulong 位型。</summary>
    public readonly long RngS0;
    /// <summary>xorshift128+ 内部状态字 s1(发牌游标),以 long 承载 ulong 位型。</summary>
    public readonly long RngS1;
    /// <summary>当前候选批所用算法序号(-1 = null)。</summary>
    public readonly int LastAlgo;
    /// <summary>当前候选批所在 tier id(int.MinValue = null)。</summary>
    public readonly int LastTierId;

    private GameSessionGenStateView(int dynamicWeight, int preDynamicWeight, int refillIndex,
        bool bcInWindow, int bcCooldown, long rngS0, long rngS1, int lastAlgo, int lastTierId)
    {
        DynamicWeight = dynamicWeight;
        PreDynamicWeight = preDynamicWeight;
        RefillIndex = refillIndex;
        BcInWindow = bcInWindow;
        BcCooldown = bcCooldown;
        RngS0 = rngS0;
        RngS1 = rngS1;
        LastAlgo = lastAlgo;
        LastTierId = lastTierId;
    }

    /// <summary>从逐局发牌器读出完整运行态(标量 + PRNG 游标 + LastAlgo/LastTierId)。</summary>
    public static GameSessionGenStateView From(DynamicWeightDiff generator)
    {
        var s = generator.ExportFullState();
        return new GameSessionGenStateView(
            s.DynamicWeight,
            s.PreDynamicWeight,
            s.RefillIndex,
            s.BcInWindow,
            s.BcCooldown,
            unchecked((long)s.RngS0),
            unchecked((long)s.RngS1),
            s.LastAlgo,
            s.LastTierId);
    }
}
