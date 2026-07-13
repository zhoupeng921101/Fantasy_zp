using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 体力溢出储存池「取用」服务端权威裁决核心(体力系统 Round G)。
///
/// 自然恢复超软上限的体力由 RecoverEnergyIfDue 路由进 PlayerDoc.EnergyOverflowPool(独立上限,不清零);
/// 玩家点「取用」把池尽量转回体力。取用量服务端派生(整池,受体力存储硬顶约束),客户端不上报——反作弊红线。
///
/// 处理顺序:
///   ① 读 doc + RecoverEnergyIfDue(先结算恢复,可能刚把 tick 路由进池,取用看到最新池);
///   ② 算取用量 withdrawn = min(池, EnergyUpperBound - 当前体力):&lt;= 0(空池 / 体力已达硬顶)→ Empty,不动数据;
///   ③ 单条原子 $inc(Energy +withdrawn) + $inc(EnergyOverflowPool -withdrawn)(CAS:池==读值 + 体力 &lt;= 硬顶-withdrawn 防越顶),
///      成功后记体力流水(+withdrawn)+ delta 推送(体力视图对账,与购买 / 许愿同源)。
///
/// 体力增走本文件原子命令(非 ChangeProperty)以与池扣同条命令保原子(不留「加了体力没扣池」);流水 / 推送手动补齐同 ChangeProperty 语义。
/// 失败一律以 ResultCode 回包,不抛异常断连;时钟统一 TimeHelper.Now。
/// 并发:同 WishHelper——当前 demo 每 UUID 只一个在线会话;CAS(池==读值)沿范式更稳。
/// 设计基线:.claude/rules/data-authority.md(资源服务端权威、限界信任、身份服务端)。
/// </summary>
public static class WithdrawOverflowHelper
{
    /// <summary>
    /// 执行取用溢出储存裁定。返回 (resultCode, energy, overflowPool, withdrawn):
    ///   - Success:energy = 取用后体力、overflowPool = 扣后池余、withdrawn = 实际取用量(&gt; 0);
    ///   - Empty:池为 0 或体力已达硬顶,withdrawn = 0,回带当前权威值;
    ///   - ServiceUnavailable:回带能读到的当前值(读失败为 0)。
    /// </summary>
    public static async FTask<(WithdrawOverflowResultCode resultCode, long energy, long overflowPool, long withdrawn)> TryWithdraw(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (WithdrawOverflowResultCode.ServiceUnavailable, 0L, 0L, 0L);
        }

        var nowMs = TimeHelper.Now;

        // ① 读 doc + 先结算被动恢复(就地更新 doc.Energy / doc.EnergyOverflowPool 为最新)。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"WithdrawOverflowHelper.TryWithdraw 读文档失败 account={accountId},err={e.Message}");
            return (WithdrawOverflowResultCode.ServiceUnavailable, 0L, 0L, 0L);
        }
        if (doc == null)
        {
            Log.Warning($"WithdrawOverflowHelper.TryWithdraw:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (WithdrawOverflowResultCode.ServiceUnavailable, 0L, 0L, 0L);
        }
        await PlayerPropertyServiceHelper.RecoverEnergyIfDue(service, accountId, doc, nowMs);

        var pool = doc.EnergyOverflowPool;
        var energy = doc.Energy;

        // ② 取用量 = min(池, 硬顶 - 体力)。空池 / 体力已达硬顶 → Empty,不动数据。
        var room = service.EnergyUpperBound - energy;
        if (room < 0L) room = 0L;
        var withdrawn = pool < room ? pool : room;
        if (withdrawn <= 0L)
        {
            return (WithdrawOverflowResultCode.Empty, energy, pool, 0L);
        }

        // ③ 单条原子:体力 +withdrawn / 池 -withdrawn(CAS 池==读值 + 体力不越硬顶)。
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.EnergyOverflowPool, pool),
            Builders<PlayerDoc>.Filter.Lte(x => x.Energy, service.EnergyUpperBound - withdrawn));
        var update = Builders<PlayerDoc>.Update
            .Inc(x => x.Energy, withdrawn)
            .Inc(x => x.EnergyOverflowPool, -withdrawn)
            .Set(x => x.LastChangeUnixMs, nowMs);
        PlayerDoc? written;
        try
        {
            written = await players.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
        }
        catch (MongoException e)
        {
            Log.Warning($"WithdrawOverflowHelper.TryWithdraw 写库失败 account={accountId} withdrawn={withdrawn},err={e.Message}");
            return (WithdrawOverflowResultCode.ServiceUnavailable, energy, pool, 0L);
        }
        if (written == null)
        {
            // CAS 未命中(并发改了池 / 体力越顶):不动数据,返 ServiceUnavailable 让客户端重试。
            Log.Warning($"WithdrawOverflowHelper.TryWithdraw CAS 未命中 account={accountId} pool={pool} energy={energy} withdrawn={withdrawn}");
            return (WithdrawOverflowResultCode.ServiceUnavailable, energy, pool, 0L);
        }

        // 记体力流水(+withdrawn)+ delta 推送(与购买 / 许愿同源,通用属性视图对账)。旁路失败仅告警不回滚。
        await AttrLedgerHelper.AppendAsync(service, accountId, PropertyType.Energy,
            balanceBefore: written.Energy - withdrawn,
            balanceAfter: written.Energy,
            delta: withdrawn,
            reasonRaw: "overflow_withdraw",
            timestampMs: nowMs);
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Energy, written.Energy, "overflow_withdraw");

        Log.Info($"取用溢出储存成功 account={accountId} withdrawn={withdrawn} energy={written.Energy} pool={written.EnergyOverflowPool}");
        return (WithdrawOverflowResultCode.Success, written.Energy, written.EnergyOverflowPool, withdrawn);
    }
}
