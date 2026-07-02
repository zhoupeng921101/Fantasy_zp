using System.Collections.Generic;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 塔罗牌合成裁决核心(塔罗收集系统,与既有 TarotBlindBox 盲盒是两个系统)。
///
/// 配置源:Luban block.TbTarotCard(牌 → 碎片道具 id + 合成所需碎片数);
/// 持久态:PlayerDoc.ItemHoldings(碎片持有)+ PlayerDoc.CollectedTarotIds(已合成集合)。
///
/// 反作弊红线:
///   - 碎片消耗量 = 服务端按表自算,**不**接受客户端上报;
///   - 「碎片足额 + 牌未合成」校验与「扣碎片 + 置牌」在**同一条**原子 FindOneAndUpdate 内
///     (filter: 持有 ≥ 所需 且 集合不含该牌;update: $inc 负扣减 + $addToSet 置牌):
///     并发同账号同牌多路请求,MongoDB 原子保证只一路命中,结构性堵死「一份碎片合两张 / 重复合成」;
///   - CAS 未命中 → 重读文档区分 AlreadyCollected / NotEnoughFragments,不发生任何写。
///
/// 旧档兼容:ItemHoldings 字段 absent 时 Gte filter 永不命中(MongoDB absent ≠ 默认值),
/// 落入重读判定按持有 0 返 NotEnoughFragments,不误判;登录链路 MigrateSchemaIfNeeded 已补空字典。
/// </summary>
public static class TarotCollectionServiceHelper
{
    /// <summary>
    /// 合成裁决。返回 (resultCode, fragmentItemId, fragmentBalance, collectedTarotIds):
    ///   - fragmentBalance = 扣减后该碎片权威持有量;-1 = 哨兵(未取到权威值,客户端不据此 set);
    ///   - collectedTarotIds = 已合成牌 id 当前全集(仅在真取自玩家文档时非 null:成功含新牌 / 失败回当前值供对齐);
    ///     null = 未取到(未读库的降级路径),handler 据此置 CollectedValid=false,客户端保留投影不清空
    ///     (proto3 repeated 无法区分「空集」与「缺失」,空列表会被客户端当权威空集误清)。
    /// </summary>
    public static async FTask<(TarotSynthesizeResultCode resultCode, int fragmentItemId, long fragmentBalance, List<int>? collectedTarotIds)>
        TrySynthesize(Scene scene, string accountId, int cardId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return (TarotSynthesizeResultCode.ServiceUnavailable, 0, -1L, null);
        }

        var card = GameConfigSystem.Tables?.TbTarotCard?.GetOrDefault(cardId);
        if (card == null)
        {
            return (TarotSynthesizeResultCode.UnknownCard, 0, -1L, null);
        }
        if (card.FragmentItemId <= 0 || card.FragmentsNeeded <= 0)
        {
            // 表行配置非法(碎片指向 / 阈值缺失):按服务不可用降级,不让非法配置变成免费合成。
            Log.Error($"TarotSynthesize 配置非法 card={cardId} fragItem={card.FragmentItemId} needed={card.FragmentsNeeded}");
            return (TarotSynthesizeResultCode.ServiceUnavailable, card.FragmentItemId, -1L, null);
        }

        var players = service.Players;
        var field = "ItemHoldings." + card.FragmentItemId;
        long needed = card.FragmentsNeeded;

        // ── 单条原子 CAS:足额 + 未合成 才 扣碎片 + 置牌 ──────────────
        var casFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Gte(field, needed),
            Builders<PlayerDoc>.Filter.Not(
                Builders<PlayerDoc>.Filter.AnyEq(x => x.CollectedTarotIds, cardId)));
        var casUpdate = Builders<PlayerDoc>.Update
            .Inc(field, -needed)
            .AddToSet(x => x.CollectedTarotIds, cardId);

        PlayerDoc? doc;
        try
        {
            doc = await players.FindOneAndUpdateAsync(casFilter, casUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotSynthesize CAS 写库失败 account={accountId} card={cardId},err={e.Message}");
            return (TarotSynthesizeResultCode.ServiceUnavailable, card.FragmentItemId, -1L, null);
        }

        if (doc != null)
        {
            // 合成成功:碎片已扣、牌已置。记道具流水(负 delta = 消耗),回权威余额 + 收集全集。
            long balance = ItemHoldingsServiceHelper.ReadHolding(doc, card.FragmentItemId);
            await ItemHoldingsServiceHelper.AppendItemLedger(
                service, accountId, card.FragmentItemId, -needed, balance, $"tarot_synthesize:card{cardId}");
            Log.Debug($"TarotSynthesize 成功 account={accountId} card={cardId} fragItem={card.FragmentItemId} spent={needed} balance={balance} collected={doc.CollectedTarotIds?.Count ?? 0}");
            return (TarotSynthesizeResultCode.Success, card.FragmentItemId, balance,
                doc.CollectedTarotIds ?? new List<int>());
        }

        // ── CAS 未命中:重读区分失败原因(不发生任何写)──────────────
        PlayerDoc? fresh;
        try
        {
            fresh = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"TarotSynthesize 重读 doc 失败 account={accountId} card={cardId},err={e.Message}");
            return (TarotSynthesizeResultCode.ServiceUnavailable, card.FragmentItemId, -1L, null);
        }
        if (fresh == null)
        {
            // 未首登(理论上登录链路已保证;实战防御)。
            return (TarotSynthesizeResultCode.ServiceUnavailable, card.FragmentItemId, -1L, null);
        }

        long holding = ItemHoldingsServiceHelper.ReadHolding(fresh, card.FragmentItemId);
        var collected = fresh.CollectedTarotIds ?? new List<int>();
        if (collected.Contains(cardId))
        {
            return (TarotSynthesizeResultCode.AlreadyCollected, card.FragmentItemId, holding, collected);
        }
        if (holding < needed)
        {
            return (TarotSynthesizeResultCode.NotEnoughFragments, card.FragmentItemId, holding, collected);
        }
        // 足额且未合成却 CAS 未命中:并发窗口内状态又变了(极罕见),按服务不可用让客户端重试。
        Log.Warning($"TarotSynthesize CAS 未命中但重读足额未合成(并发窗口) account={accountId} card={cardId} holding={holding}");
        return (TarotSynthesizeResultCode.ServiceUnavailable, card.FragmentItemId, holding, collected);
    }
}
