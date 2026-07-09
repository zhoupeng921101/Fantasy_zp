using System.Collections.Generic;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 塔罗牌解锁奖励发放(服务端权威,挂在塔罗购买成功之后)。两层奖励均引用固定礼包表 TbGiftFixed,统一走同一发奖器:
///   - 每碎片:每成功推进一步,发放该牌 ShardReward 指向的固定礼包;
///   - 单卡集齐:进度刚达到总步数(newSteps == totalSteps)时,发放该牌 CollectReward 指向的固定礼包。
/// reward_id = 0 表示该层无奖励。
///
/// 发放语义(GrantRewardById):按 reward_id 取 TbGiftFixed 中该组**全部**道具项(固定、不按权重抽),逐项发放——
///   - automatic 的货币道具(TbItemDef.Automatic==1 且 UseEffect==1 num,如体力道具 30002)→ 直接转为对应货币
///     (复用 InventoryServiceHelper.TryResolveCurrencyProduce 的 num→PropertyType 映射,即「发放即自动使用」),不入背包;
///   - 其余道具(材料、非 automatic 货币道具等)→ 入背包(ItemHoldings)。
/// 故「碎片发体力」由固定礼包组内放一个体力货币道具承载,不再写死固定体力值。automatic 的非货币效果道具(图案 / 嵌套礼包)
/// 本轮不自动应用,按普通道具入背包(与既有发放路径一致——服务端发放侧不处理 automatic 的图案/开箱效果)。
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
        if (card.ShardReward > 0)
        {
            await GrantRewardByIdAsync(scene, service, accountId, card.ShardReward, $"tarot_shard:card{cardId}:step{newSteps}");
        }

        // ── 单卡集齐奖励 ────────────────────────────────────────────
        if (totalSteps > 0 && newSteps == totalSteps && card.CollectReward > 0)
        {
            await GrantRewardByIdAsync(scene, service, accountId, card.CollectReward, $"tarot_collect:card{cardId}");
        }
    }

    /// <summary>
    /// 按固定礼包 reward_id 发放一份奖励:取 TbGiftFixed 该组全部道具项,逐项发放
    /// (automatic 货币道具转货币 + 推送;其余入背包 + 统一推背包)。逐项 best-effort,失败记日志不抛。
    /// </summary>
    private static async FTask GrantRewardByIdAsync(
        Scene scene, PlayerPropertyServiceComponent service, string accountId, int rewardId, string reason)
    {
        var items = ResolveFixedReward(rewardId);
        if (items.Count == 0)
        {
            Log.Warning($"TarotReward 固定礼包为空(reward_id 未配置/无有效行) account={accountId} reward={rewardId}");
            return;
        }

        bool anyBagGrant = false;
        foreach (var (itemId, count) in items)
        {
            var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(itemId);

            // automatic 货币道具:发放即转对应货币(+体力 / +虔诚币等),不入背包。
            if (def != null && def.Automatic == 1
                && InventoryServiceHelper.TryResolveCurrencyProduce(def, out var propType, out var perItem))
            {
                long delta = perItem * count;
                if (delta <= 0)
                {
                    continue;
                }
                var (code, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
                    scene, accountId, propType, delta, reason, serverAuthoritative: true);
                if (code == PropertyChangeResultCode.Success)
                {
                    PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, propType, newAmount, reason);
                }
                else
                {
                    // 到顶(OverLimit)等非成功码不算错(如体力已满仍可推进进度);仅记录供排查。
                    Log.Debug($"TarotReward 货币奖励未发放 account={accountId} reward={rewardId} item={itemId} type={propType} delta={delta} code={code}");
                }
                continue;
            }

            // 其余道具入背包。
            var (ok, _) = await ItemHoldingsServiceHelper.GrantItem(service, accountId, itemId, count, reason);
            if (ok)
            {
                anyBagGrant = true;
            }
            else
            {
                Log.Error($"TarotReward 发放道具失败 account={accountId} reward={rewardId} item={itemId} count={count}");
            }
        }

        if (!anyBagGrant)
        {
            return;
        }

        // 有道具入背包 → 读一次权威文档,整份推送背包投影(GrantItem 逐项不推,统一在此推一次)。
        PlayerDoc? doc;
        try
        {
            doc = await service.Players
                .Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId))
                .FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotReward 读文档推背包失败(道具已落账,下次登录快照对齐) account={accountId} reward={rewardId},err={e.Message}");
            return;
        }
        InventoryServiceHelper.SendInventoryDeltaPush(scene, accountId, doc);
    }

    /// <summary>取固定礼包表 TbGiftFixed 中某 reward_id 的全部道具项(item_id × num,item_id/num&gt;0 才收);无配置返回空列表。</summary>
    private static List<(int itemId, long count)> ResolveFixedReward(int rewardId)
    {
        var result = new List<(int itemId, long count)>();
        var list = GameConfigSystem.Tables?.TbGiftFixed?.DataList;
        if (list == null)
        {
            return result;
        }
        foreach (var row in list)
        {
            if (row.RewardId == rewardId && row.ItemId > 0 && row.Num > 0)
            {
                result.Add((row.ItemId, row.Num));
            }
        }
        return result;
    }
}
