using Fantasy.Async;
using GameConfig.reward;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 固定奖励盒发放器(服务端权威,可复用)。按 box_id 取 TbRewardBox 该盒的 Rewards,整组固定全发(不按权重抽):
///   - Currency 条目:TargetId 指 TbCurrency 资源 id,经 InventoryServiceHelper.MapCurrencyTypeToProperty 映射到服务端 PropertyType,
///     走 PlayerPropertyServiceHelper.ChangeProperty(serverAuthoritative)落账 + SendDeltaPushTo 推送。
///     num 无配置 / 类型无对应 PropertyType(未来未建模货币类型)→ 记 Error 跳过,绝不误发。
///   - Item 条目:TargetId 指 TbItemDef 道具 id,走 ItemHoldingsServiceHelper.GrantItem 入背包;
///     本盒任一道具入包成功后,读一次玩家文档统一 SendInventoryDeltaPush 一次(GrantItem 逐项不推)。
/// 逐项 best-effort:单项失败记 Error 不抛、不影响其余项。空盒(box_id 未配置 / Rewards 空)记 Warning 返回。
/// 别处(塔罗碎片/集齐、章节完成等)需发固定奖励一律调本发奖器,不各自另写发放逻辑。
/// </summary>
public static class RewardBoxServiceHelper
{
    /// <summary>
    /// 发放 box_id 对应的整盒固定奖励。逐项 best-effort;有道具入包则末尾统一推一次背包投影。
    /// </summary>
    public static async FTask GrantBoxAsync(
        Scene scene, PlayerPropertyServiceComponent service, string accountId, int boxId, string reason)
    {
        var box = GameConfigSystem.Tables?.TbRewardBox?.GetOrDefault(boxId);
        if (box?.Rewards == null || box.Rewards.Count == 0)
        {
            Log.Warning($"RewardBox 发放:盒子为空(box_id 未配置/无奖励项) account={accountId} box={boxId}");
            return;
        }

        bool anyBagGrant = false;
        foreach (var entry in box.Rewards)
        {
            if (entry.Amount <= 0)
            {
                continue;
            }

            // 货币:num_id → PropertyType(复用 InventoryServiceHelper 的权威映射),ChangeProperty 落账 + 推送。
            if (entry.RewardType == ERewardType.Currency)
            {
                var cur = GameConfigSystem.Tables?.TbCurrency?.GetOrDefault(entry.TargetId);
                if (cur == null)
                {
                    Log.Error($"RewardBox 货币奖励:currency 未配置 account={accountId} box={boxId} currencyId={entry.TargetId}");
                    continue;
                }
                if (!InventoryServiceHelper.MapCurrencyTypeToProperty(cur.CurrencyType, out var propType))
                {
                    Log.Error($"RewardBox 货币奖励:currency 类型无对应 PropertyType(未来未建模货币类型) account={accountId} box={boxId} currencyId={entry.TargetId} currencyType={cur.CurrencyType}");
                    continue;
                }
                var (code, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
                    scene, accountId, propType, entry.Amount, reason, serverAuthoritative: true);
                if (code == PropertyChangeResultCode.Success)
                {
                    PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, propType, newAmount, reason);
                }
                else
                {
                    // 到顶(OverLimit)等非成功码不算错(如体力已满);仅记录供排查。
                    Log.Debug($"RewardBox 货币奖励未发放 account={accountId} box={boxId} type={propType} delta={entry.Amount} code={code}");
                }
                continue;
            }

            // 道具:入背包(逐项不推,末尾统一推一次)。
            if (entry.RewardType == ERewardType.Item)
            {
                var (ok, _) = await ItemHoldingsServiceHelper.GrantItem(service, accountId, entry.TargetId, entry.Amount, reason);
                if (ok)
                {
                    anyBagGrant = true;
                }
                else
                {
                    Log.Error($"RewardBox 发放道具失败 account={accountId} box={boxId} item={entry.TargetId} count={entry.Amount}");
                }
                continue;
            }

            Log.Error($"RewardBox 未知奖励类型 account={accountId} box={boxId} type={entry.RewardType}");
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
            Log.Warning($"RewardBox 读文档推背包失败(道具已落账,下次登录快照对齐) account={accountId} box={boxId},err={e.Message}");
            return;
        }
        InventoryServiceHelper.SendInventoryDeltaPush(scene, accountId, doc);
    }
}
