using System.Collections.Generic;
using Fantasy.Async;
using GameConfig.reward;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 邮件领取奖励的服务端权威发放器。领取(邮件 / 排行榜结算 / 活动结算 / GM 发信,均经 SendMailTo→领取)裁定的奖励
/// 必须在服务端就地到账(写玩家文档 + 推送),而非只回包让客户端本地落地——后者在客户端无活跃合成局时静默丢奖、
/// 且奖励从不进服务端账本(违 data-authority)。
///
/// 输入 = 邮件内嵌的 RewardEntryDoc 列表(类型 + 目标 id + 数量),全部发放,逐项 best-effort(单项失败记日志不抛、不影响其余):
///   - Currency(RewardType=1):TargetId 指 TbCurrency 资源 id → InventoryServiceHelper.MapCurrencyTypeToProperty 映射服务端 PropertyType
///     → ChangeProperty(serverAuthoritative) + SendDeltaPushTo;cur 未配置 / 类型无对应 PropertyType(如 EXP)→ 记 Error 跳过,绝不误发。
///   - Item(RewardType=2):TargetId 指 TbItemDef 道具 id → ItemHoldingsServiceHelper.GrantItem 入背包;
///     任一入包后末尾统一 SendInventoryDeltaPush 一次。
/// 分派口径与 RewardBoxServiceHelper(固定奖励盒,读 Luban RewardEntry)同构:两者都是「内联奖励条目列表、全部发放」,
/// 差别仅输入来源(本发放器吃运行态 RewardEntryDoc:邮件内嵌 / 种子 / GM 解析)。
///
/// 事务边界:发放与领取防重(mail_record 唯一写)不在同一事务,只在 Claim 已认领(防重写成功)之后由 Handler
/// 在 Success 分支调用。发奖未完成时邮件已定格为已领,存在极小概率奖励丢失窗口(本轮不做登录对账补领,同 TarotRewardServiceHelper 权衡)。
/// </summary>
public static class MailRewardGrantHelper
{
    /// <summary>
    /// 发放一封邮件领取的内联奖励条目列表。逐项 best-effort;有道具入背包则末尾统一推一次背包投影。
    /// scene 内取 PlayerPropertyServiceComponent(缺失记 Error 返回,不误发);reason 供流水与日志溯源。
    /// </summary>
    public static async FTask GrantRewardsAsync(
        Scene scene, string accountId, IReadOnlyList<RewardEntryDoc> rewards, string reason)
    {
        if (rewards == null || rewards.Count == 0)
        {
            return;
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error($"邮件奖励发放:Scene 无 PlayerPropertyServiceComponent,奖励未发放 account={accountId} reason={reason}");
            return;
        }

        bool anyBagGrant = false;
        foreach (var reward in rewards)
        {
            if (reward == null || reward.Amount <= 0)
            {
                continue;
            }

            // 货币:TargetId → TbCurrency → PropertyType(复用 InventoryServiceHelper 权威映射),ChangeProperty 落账 + 推送。
            if (reward.RewardType == (int)ERewardType.Currency)
            {
                var cur = GameConfigSystem.Tables?.TbCurrency?.GetOrDefault(reward.TargetId);
                if (cur == null)
                {
                    Log.Error($"邮件奖励货币:cur 未配置 account={accountId} currencyId={reward.TargetId} reason={reason}");
                    continue;
                }
                if (!InventoryServiceHelper.MapCurrencyTypeToProperty(cur.CurrencyType, out var propType))
                {
                    Log.Error($"邮件奖励货币:cur 类型无对应 PropertyType(如 EXP) account={accountId} currencyId={reward.TargetId} currencyType={cur.CurrencyType} reason={reason}");
                    continue;
                }
                var (code, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
                    scene, accountId, propType, reward.Amount, reason, serverAuthoritative: true);
                if (code == PropertyChangeResultCode.Success)
                {
                    PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, propType, newAmount, reason);
                }
                else
                {
                    // 到顶(OverLimit)等非成功码不算错(如体力已满);仅记录供排查。
                    Log.Debug($"邮件奖励货币未发放 account={accountId} currencyId={reward.TargetId} type={propType} delta={reward.Amount} code={code}");
                }
                continue;
            }

            // 道具:TargetId 当 itemId 入背包(逐项不推,末尾统一推一次)。
            if (reward.RewardType == (int)ERewardType.Item)
            {
                var (ok, _) = await ItemHoldingsServiceHelper.GrantItem(service, accountId, reward.TargetId, reward.Amount, reason);
                if (ok)
                {
                    anyBagGrant = true;
                }
                else
                {
                    Log.Error($"邮件奖励发放道具失败 account={accountId} item={reward.TargetId} count={reward.Amount} reason={reason}");
                }
                continue;
            }

            Log.Error($"邮件奖励未知类型 account={accountId} rewardType={reward.RewardType} targetId={reward.TargetId} reason={reason}");
        }

        if (!anyBagGrant)
        {
            return;
        }

        // 有道具入背包 → 读一次权威文档,整份推送背包投影(GrantItem 逐项不推,统一在此推一次;仿 RewardBoxServiceHelper)。
        PlayerDoc? doc;
        try
        {
            doc = await service.Players
                .Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId))
                .FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"邮件奖励读文档推背包失败(道具已落账,下次登录快照对齐) account={accountId} reason={reason},err={e.Message}");
            return;
        }
        InventoryServiceHelper.SendInventoryDeltaPush(scene, accountId, doc);
    }
}
