using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 塔罗牌购买进度请求入口(Outer RPC,运行在 Gate Scene,塔罗收集系统)。
///
/// 玩家身份从会话取(同 C2G_DeliverOrderRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求只携带牌 id,**不**接受成本(服务端按 TbTarotCard.UnlockCosts[当前步] 自算 — 反作弊红线)。
/// 校验 / 原子扣虔诚币加进度由 TarotCollectionServiceHelper.TryPurchaseStep 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
/// 成功扣币后额外起虔诚币增量推送,使 HUD 货币视图与购买结果一致(同 ChangeProperty 成功后 SendDeltaPushTo 范式)。
/// </summary>
public sealed class C2G_TarotPurchaseRequestHandler : MessageRPC<C2G_TarotPurchaseRequest, G2C_TarotPurchaseResponse>
{
    protected override async FTask Run(Session session, C2G_TarotPurchaseRequest request,
        G2C_TarotPurchaseResponse response, Action reply)
    {
        response.CardId = request.CardId;
        response.Steps = -1;            // 哨兵:未裁决/未取到权威值,客户端不据此 set
        response.PietyBalance = -1L;    // 哨兵:同上

        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        var accountName = account?.Name;
        if (string.IsNullOrEmpty(accountName))
        {
            Log.Warning("收到 TarotPurchaseRequest 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = TarotPurchaseResultCode.NotLoggedIn;
            return;
        }

        var (resultCode, steps, pietyBalance, progress) =
            await TarotCollectionServiceHelper.TryPurchaseStep(session.Scene, accountName, request.CardId);

        response.ResultCode = resultCode;
        response.Steps = steps;
        response.PietyBalance = pietyBalance;
        // progress == null = 未取到玩家文档(降级路径):ProgressValid=false,客户端保留投影不清空;
        // 非 null(含空字典 = 权威空进度)才可信,客户端整份覆盖。
        response.ProgressValid = progress != null;
        if (progress != null)
        {
            foreach (var kv in progress)
            {
                var entry = TarotProgressEntry.Create();
                entry.CardId = kv.Key;
                entry.Steps = kv.Value;
                response.ProgressAll.Add(entry);
            }
        }

        // 成功扣了虔诚币 → 推送权威余额,保持 HUD 货币视图一致(离线/无会话时静默丢弃)。
        if (resultCode == TarotPurchaseResultCode.Success && pietyBalance >= 0L)
        {
            PlayerPropertyServiceHelper.SendDeltaPushTo(
                session.Scene, accountName, PropertyType.Piety, pietyBalance, $"tarot_purchase:card{request.CardId}");
        }

        // 成功推进一步 → 发放解锁奖励(每碎片体力 + 刚集齐则发奖励道具包)。
        // steps = 购买后新步数;total = 该牌总步数(= UnlockCosts.Count,集齐阈值)。
        // 奖励与购买 CAS 非同一事务,发放器内 best-effort(失败记 Error 不回滚进度)。
        if (resultCode == TarotPurchaseResultCode.Success)
        {
            var card = GameConfigSystem.Tables?.TbTarotCard?.GetOrDefault(request.CardId);
            int total = card?.UnlockCosts?.Count ?? 0;
            await TarotRewardServiceHelper.GrantStepRewardsAsync(session.Scene, accountName, request.CardId, steps, total);
        }
    }
}
