using Fantasy.Async;

namespace Fantasy;

/// <summary>
/// 塔罗牌解锁奖励发放(服务端权威,挂在塔罗购买成功之后)。两层奖励均引用固定奖励盒表 TbRewardBox 的 box_id,统一走同一发奖器:
///   - 每碎片:每成功推进一步(推进到第 newSteps 步),发放该牌 ShardReward[newSteps-1] 指向的奖励盒(逐碎片数组,越界/0=无);
///   - 单卡集齐:进度刚达到总步数(newSteps == totalSteps)时,发放该牌 CollectReward 指向的奖励盒。
/// box_id = 0 表示该层无奖励。
///
/// 发放语义:委托可复用的 RewardBoxServiceHelper.GrantBoxAsync——按 box_id 取该盒 Rewards 整组固定全发(不按权重抽),
/// 按 RewardEntry 显式类型分派:Currency 条目发对应货币(num_id → PropertyType 映射 + ChangeProperty + 推送)、
/// Item 条目入背包(逐项不推,末尾统一推一次)。故「碎片发体力」由盒内放一条 Currency(体力)条目承载,不再写死固定体力值。
///
/// 事务边界:奖励经独立的 ChangeProperty / GrantItem 原子写发放,与购买 CAS(TarotCollectionServiceHelper)
/// **不在同一事务**。购买成功后进程崩溃 / 弱网导致发奖未完成时,进度已定格、后续购买返回 AlreadyMaxed 不重发,
/// 存在极小概率的奖励丢失窗口(本轮不做登录对账补领)。逐项发放 best-effort:失败记 Error 不抛、不回滚进度。
///
/// 幂等:「刚集齐」由购买 CAS 的唯一一次 Success 跨越阈值保证——重复点击已满的牌在裁决核心即返 AlreadyMaxed
/// (非 Success),不会再次进入本发放器,故集齐奖励天然只发一次。
/// </summary>
public static class TarotRewardServiceHelper
{
    /// <summary>
    /// 发放一次成功购买对应的奖励。newSteps = 该牌购买后已达步数,totalSteps = 该牌总步数(集齐阈值)。
    /// 调用方须已确认本次为 Success(恰好推进一步),否则不应调用(避免对失败 / 幂等命中误发)。
    /// </summary>
    public static async FTask GrantStepRewardsAsync(Scene scene, string accountId, int cardId, int newSteps, int totalSteps)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            Log.Error($"TarotReward 发放失败:Scene 无 PlayerPropertyServiceComponent account={accountId} card={cardId}");
            return;
        }

        var card = GameConfigSystem.Tables?.TbTarotCard?.GetOrDefault(cardId);
        if (card == null)
        {
            return; // 成功购买必对应合法牌;防御性返回。
        }

        // ── 每碎片奖励 ──────────────────────────────────────────────
        // ShardReward 为逐碎片数组:索引 i 对应第 i+1 个碎片。本次推进到第 newSteps 步,取索引 newSteps-1。
        // 数组长度可短于总步数,越界或值为 0 表示该碎片无奖励。
        var shardRewards = card.ShardReward;
        if (shardRewards != null && newSteps >= 1 && newSteps <= shardRewards.Count)
        {
            int shardBox = shardRewards[newSteps - 1];
            if (shardBox > 0)
            {
                await RewardBoxServiceHelper.GrantBoxAsync(scene, service, accountId, shardBox, $"tarot_shard:card{cardId}:step{newSteps}");
            }
        }

        // ── 单卡集齐奖励 ────────────────────────────────────────────
        if (totalSteps > 0 && newSteps == totalSteps && card.CollectReward > 0)
        {
            await RewardBoxServiceHelper.GrantBoxAsync(scene, service, accountId, card.CollectReward, $"tarot_collect:card{cardId}");
        }
    }
}
