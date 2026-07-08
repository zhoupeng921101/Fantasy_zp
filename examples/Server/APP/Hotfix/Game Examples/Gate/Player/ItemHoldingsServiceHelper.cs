using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家道具持有裁决核心(服务端唯一权威,持久于 PlayerDoc.ItemHoldings 字典)。
///
/// 写路径:单条原子 FindOneAndUpdate 走 "ItemHoldings.&lt;itemId&gt;" 点路径 $inc(MongoDB 自动创建缺失的嵌套路径),
/// 并发同账号多路发放各自原子累加、无丢失更新。发放只有服务端权威来源(订单交付掉道具等),
/// **不**存在客户端直接请求加道具的 RPC 路径 —— 反作弊面在业务入口(交付 CAS / 使用事务)收口,本 helper 不设频率闸。
///
/// 流水:写库成功后旁路追加 player_item_ledger 一行(insert-only);ledger 失败 Warning 不回滚持有
/// (沿 player_attr_ledger「ledger 失败不回滚」基线)。
///
/// 消耗路径不在本 helper:道具使用的「持有足额校验 + 扣减 + 产出」必须与判定同一条原子命令,
/// 见背包使用事务(InventoryServiceHelper);扣减成功后同样走 AppendItemLedger 记流水。
/// </summary>
public static class ItemHoldingsServiceHelper
{
    /// <summary>
    /// 服务端权威发放道具(itemId × count 累加到持有字典)。
    /// 返回 (ok, balanceAfter):ok=false 时 balanceAfter=-1(哨兵,调用方不据此 set);
    /// ok=true 时 balanceAfter = 落账后该道具权威持有量。
    /// 入参非法(itemId/count ≤ 0)或 MongoDB 不可达 → (false, -1),不抛。
    /// </summary>
    public static async FTask<(bool ok, long balanceAfter)> GrantItem(
        PlayerPropertyServiceComponent service, string accountId, int itemId, long count, string reason)
    {
        if (service?.Players == null || string.IsNullOrEmpty(accountId) || itemId <= 0 || count <= 0)
        {
            return (false, -1L);
        }

        var field = "ItemHoldings." + itemId;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = Builders<PlayerDoc>.Update.Inc(field, count);
        try
        {
            var doc = await service.Players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (doc == null)
            {
                // 未首登(登录链路理论已保证 doc 存在;实战防御)。
                Log.Warning($"ItemHoldingsServiceHelper.GrantItem 未命中玩家文档 account={accountId} item={itemId}");
                return (false, -1L);
            }
            long after = ReadHolding(doc, itemId);
            await AppendItemLedger(service, accountId, itemId, count, after, reason);
            return (true, after);
        }
        catch (MongoException e)
        {
            Log.Warning($"ItemHoldingsServiceHelper.GrantItem 写库失败 account={accountId} item={itemId} count={count},err={e.Message}");
            return (false, -1L);
        }
    }

    /// <summary>从 doc 读某道具当前持有量(字典键 = itemId 十进制字符串;缺键 = 0)。</summary>
    public static long ReadHolding(PlayerDoc doc, int itemId)
    {
        if (doc?.ItemHoldings == null) return 0L;
        return doc.ItemHoldings.TryGetValue(itemId.ToString(), out var v) ? v : 0L;
    }

    /// <summary>
    /// 道具流水旁路追加(insert-only,永不 update / delete)。调用方 await 本方法后再回响应,
    /// 保证流水先于响应/推送落库(沿 player_attr_ledger「ledger 先于推送」同一审计口径)。
    /// ItemLedger 句柄 null(MongoDB 不可达启动时未绑)→ 静默跳过;写失败 Warning 不抛、不回滚持有。
    /// 发放与消耗共用(delta 有符号:正 = 获得 / 负 = 消耗)。
    /// </summary>
    public static async FTask AppendItemLedger(
        PlayerPropertyServiceComponent service, string accountId, int itemId, long delta, long balanceAfter, string reason)
    {
        var ledger = service.ItemLedger;
        if (ledger == null) return;

        var row = new PlayerItemLedgerDoc
        {
            Timestamp = TimeHelper.Now,
            Account = accountId,
            ItemId = itemId,
            Delta = delta,
            BalanceAfter = balanceAfter,
            ReasonRaw = reason ?? string.Empty,
            SchemaVersion = 1,
        };
        try
        {
            await ledger.InsertOneAsync(row);
        }
        catch (MongoException e)
        {
            Log.Warning($"ItemHoldingsServiceHelper.AppendItemLedger 写流水失败(持有不回滚) account={accountId} item={itemId} delta={delta},err={e.Message}");
        }
    }
}
