using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 进入主游戏阶段的一次性拉取:登录 RPC 完成、主游戏 UI 即将就绪时由客户端发起,
/// 服务端回带订单快照,客户端整份覆盖本地视图。
///
/// 身份从会话取(GateAccountFlagComponent.Account.PlayerId);请求无字段。
/// 订单段服务不可用 / 玩家文档读不到 → 空 ActiveOrders 占位(MergeOrderServiceHelper 派生基线)。
/// 未挂会话身份(GateAccountFlagComponent 缺失 / Account.PlayerId 空)→ 空订单快照占位。
///
/// 局内对局现场(盘面 / cosmetic + 合成经济叠加层切片)不再走本请求 —— 由 GameStart(续局)/ GameSnapshot 回带,
/// 见 BlockBlastGameMessage.proto。
/// </summary>
public sealed class C2G_EnterMainGameRequestHandler
    : MessageRPC<C2G_EnterMainGameRequest, G2C_EnterMainGameResponse>
{
    protected override async FTask Run(Session session, C2G_EnterMainGameRequest request,
        G2C_EnterMainGameResponse response, Action reply)
    {
        // 默认空 snapshot 占位:失败时客户端整份覆盖、不显示残留订单。
        response.OrderSnapshot = MergeOrderSnapshot.Create();

        // 会话身份取用:沿 C2G_PropertyChangeRequestHandler 同范式。
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        if (account == null || string.IsNullOrEmpty(account.PlayerId))
        {
            Log.Warning("收到 EnterMainGameRequest 但会话未登录(无 GateAccountFlagComponent/Account/PlayerId),无法确定身份。");
            return;
        }

        var playerId = account.PlayerId;
        // Account.Name 即 accountId(登录链路 AccountManageHelper.Add 的 key,与 PlayerDoc.AccountId 同口径)。
        var accountName = account.Name;

        // ── 订单段 ──────────────────────────────────────────────
        // 取 PlayerPropertyServiceComponent + 读 PlayerDoc,跑刷新结算(到点自动推进 cursor / 清 mask)再派生快照。
        // 与原登录 handler 末尾 ApplyOrderRefreshIfDue + BuildSnapshot 同口径,只是把推送改成同包回带。
        var propService = session.Scene.GetComponent<PlayerPropertyServiceComponent>();
        if (propService != null && propService.Players != null && !string.IsNullOrEmpty(accountName))
        {
            PlayerDoc? playerDoc = null;
            try
            {
                playerDoc = await propService.Players
                    .Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountName))
                    .FirstOrDefaultAsync();
            }
            catch (MongoException e)
            {
                // 读失败:Warning,订单段空快照降级(响应已含 MergeOrderSnapshot.Create() 占位)。
                Log.Warning($"EnterMainGame 读 PlayerDoc 失败 account={accountName},err={e.Message}");
            }

            if (playerDoc != null)
            {
                await MergeOrderServiceHelper.ApplyOrderRefreshIfDue(
                    propService, accountName, playerDoc, Fantasy.Helper.TimeHelper.Now);
                response.OrderSnapshot = MergeOrderServiceHelper.BuildSnapshot(playerDoc);

                // ── 道具持有 + 塔罗进度整份快照(客户端整份覆盖本地投影)──────────
                // ItemDataLoaded 仅在真读到玩家文档时置 true:proto3 repeated 无法区分「权威空集」与
                // 「降级未取到」,读库失败若不置此标志,客户端会把空列表当权威空集误清背包/进度投影。
                response.ItemDataLoaded = true;
                if (playerDoc.ItemHoldings != null)
                {
                    foreach (var kv in playerDoc.ItemHoldings)
                    {
                        if (!int.TryParse(kv.Key, out var itemId) || itemId <= 0 || kv.Value <= 0) continue;
                        var holding = ItemHolding.Create();
                        holding.ItemId = itemId;
                        holding.Count = kv.Value;
                        response.ItemHoldings.Add(holding);
                    }
                }
                if (playerDoc.TarotProgress != null)
                {
                    foreach (var kv in playerDoc.TarotProgress)
                    {
                        if (!int.TryParse(kv.Key, out var tarotId) || tarotId <= 0 || kv.Value <= 0) continue;
                        var entry = TarotProgressEntry.Create();
                        entry.CardId = tarotId;
                        entry.Steps = kv.Value;
                        response.TarotProgress.Add(entry);
                    }
                }
            }
            // playerDoc == null:沿用上面默认空 snapshot / 空持有列表。
        }
        // propService == null / Players == null:同样沿用空 snapshot / 空持有列表。

        Log.Debug($"EnterMainGame playerId={playerId} account={accountName} " +
                  $"orderActiveCount={response.OrderSnapshot.ActiveOrders.Count} " +
                  $"itemHoldings={response.ItemHoldings.Count} tarotProgress={response.TarotProgress.Count}");
    }
}
