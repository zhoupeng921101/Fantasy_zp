using GameLogic.BlockBlast;

namespace Fantasy;

/// <summary>
/// 发牌器状态向量的只读快照,供 Hotfix 侧组装协议 BlockGenState。
/// 置于 Entity 程序集:DynamicWeightDiff 的 preDynamicWeight / refillIndex 仅以 internal 暴露
/// (InternalPreDynamicWeight / InternalRefillIndex),与发牌器同程序集才可读;Hotfix 是独立程序集读不到。
/// 故跨手累积态的取值在此处完成,Hotfix 只消费本结构。
/// </summary>
public readonly struct GameSessionGenStateView
{
    public readonly int DynamicWeight;
    public readonly int PreDynamicWeight;
    public readonly int RefillIndex;
    public readonly bool BcInWindow;
    public readonly int BcCooldown;

    private GameSessionGenStateView(int dynamicWeight, int preDynamicWeight, int refillIndex,
        bool bcInWindow, int bcCooldown)
    {
        DynamicWeight = dynamicWeight;
        PreDynamicWeight = preDynamicWeight;
        RefillIndex = refillIndex;
        BcInWindow = bcInWindow;
        BcCooldown = bcCooldown;
    }

    /// <summary>从逐局发牌器读出完整跨手累积态(含 internal 暴露的 pre / refillIndex)。</summary>
    public static GameSessionGenStateView From(DynamicWeightDiff generator)
    {
        var bc = generator.GetBoardClearState();
        return new GameSessionGenStateView(
            generator.DynamicWeight,
            generator.InternalPreDynamicWeight,
            generator.InternalRefillIndex,
            bc.inWindow,
            bc.cooldown);
    }
}
