using System.Collections.Generic;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 邮件领取奖励的服务端权威发放器。领取(邮件 / 排行榜结算 / 活动结算 / GM 发信,均经 SendMailTo→领取)裁定的奖励
/// 必须在服务端就地到账(写玩家文档 + 推送),而非只回包让客户端本地落地——后者在客户端无活跃合成局时静默丢奖、
/// 且奖励从不进服务端账本(违 data-authority)。
///
/// 输入 = MailDecisionHelper.Claim 服务端抽出的道具列表(道具 id × 数量)。按客户端 GameLogic.ItemGrant 同一语义分派
/// (Automatic 分流 + UseEffect 分派),服务端只落其能权威到账的两类,逐项 best-effort(单项失败记日志不抛、不影响其余):
///   - 纯持有道具(Automatic != 1):ItemHoldingsServiceHelper.GrantItem 入背包;任一入包后末尾统一 SendInventoryDeltaPush 一次。
///   - 立即结算货币(Automatic == 1 且 UseEffect==1 num,且 num 类型有对应服务端 PropertyType):
///     TryResolveCurrencyProduce → ChangeProperty(serverAuthoritative) + SendDeltaPushTo。
/// 服务端无权威到账入口的(Automatic==1 的图案 UseEffect=2 / 自选礼包 3 / 随机礼包 4 / EVENT 头像 5 /
///   num 类型不支持的货币如 EXP):记 Warning 跳过、绝不误发(留客户端局内路径或后续专项接入)。
///
/// 事务边界:发放与领取防重(mail_record 唯一写)不在同一事务,只在 Claim 已认领(防重写成功 + 抽奖)之后由 Handler
/// 在 Success 分支调用。发奖未完成时邮件已定格为已领,存在极小概率奖励丢失窗口(本轮不做登录对账补领,同 TarotRewardServiceHelper 权衡)。
/// 别处发固定奖励盒仍走 RewardBoxServiceHelper;本发放器专司 gift_pool 随机库抽出结果的到账,两者是不同发放语义,不互相替代。
/// </summary>
public static class MailRewardGrantHelper
{
    /// <summary>
    /// 发放一封邮件领取抽出的奖励列表。逐项 best-effort;有道具入背包则末尾统一推一次背包投影。
    /// scene 内取 PlayerPropertyServiceComponent(缺失记 Error 返回,不误发);reason 供流水与日志溯源。
    /// </summary>
    public static async FTask GrantRewardsAsync(
        Scene scene, string accountId, IReadOnlyList<MailRewardItem> rewards, string reason)
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
            if (reward == null || reward.Count <= 0)
            {
                continue;
            }

            var def = GameConfigSystem.Tables?.TbItemDef?.GetOrDefault(reward.ItemId);
            if (def == null)
            {
                Log.Error($"邮件奖励发放:道具未配置 account={accountId} item={reward.ItemId} reason={reason}");
                continue;
            }

            // 立即结算货币(Automatic==1 且 UseEffect==1 num,类型有对应 PropertyType):权威落账 + 推送。
            if (def.Automatic == 1
                && InventoryServiceHelper.TryResolveCurrencyProduce(def, out var propType, out var perItem))
            {
                long delta = perItem * reward.Count;
                var (code, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
                    scene, accountId, propType, delta, reason, serverAuthoritative: true);
                if (code == PropertyChangeResultCode.Success)
                {
                    PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, propType, newAmount, reason);
                }
                else
                {
                    // 到顶(OverLimit)等非成功码不算错(如体力已满);仅记录供排查。
                    Log.Debug($"邮件奖励货币未发放 account={accountId} item={reward.ItemId} type={propType} delta={delta} code={code}");
                }
                continue;
            }

            // 立即结算但服务端无权威到账入口(图案 / 自选 / 随机礼包 / EVENT 头像 / num 类型不支持的货币):记 Warning 跳过,绝不误发。
            if (def.Automatic == 1)
            {
                Log.Warning($"邮件奖励含服务端无法权威到账的类型(UseEffect={def.UseEffect}),跳过 account={accountId} item={reward.ItemId} reason={reason}");
                continue;
            }

            // 纯持有道具(Automatic != 1):入背包(逐项不推,末尾统一推一次)。
            var (ok, _) = await ItemHoldingsServiceHelper.GrantItem(service, accountId, reward.ItemId, reward.Count, reason);
            if (ok)
            {
                anyBagGrant = true;
            }
            else
            {
                Log.Error($"邮件奖励发放道具失败 account={accountId} item={reward.ItemId} count={reward.Count} reason={reason}");
            }
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
