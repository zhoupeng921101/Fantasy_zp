using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 钻石购买体力(命运能量)服务端权威裁决核心(体力系统 Round D + Round F 每日限次)。
///
/// 单价 / 发放量 / 每日上限服务端派生(BuyEnergyConfigServer),客户端请求不带数值——反作弊红线,同改名费(RenameHelper)范式。
/// 处理顺序(原子性:先判每日闸再检空间再扣钻再发体力,失败不留半成品):
///   ① 读 doc + 懒每日重置(ResetBuyEnergyIfDue,跨服务端本地日则 BuyEnergyUsedToday=0);
///   ② 每日门控:重置后 BuyEnergyUsedToday >= DailyLimit → DailyLimitReached,不扣钻、不发、不消耗次数;
///   ③ 读当前体力(ReadEnergyAuthoritative 已结算离线恢复)——服务不可用短路;
///   ④ 检体力空间:当前体力 + 发放量 > EnergyUpperBound(存储硬上界)→ OverLimit,不扣钻、不发、不消耗次数;
///   ⑤ 扣钻:ChangeProperty(Diamond, -单价, serverAuthoritative, reason="energy_purchase")。
///      钻不足 → NotEnoughDiamond、不发体力;其它失败 → ServiceUnavailable;
///   ⑥ 发体力:ChangeProperty(Energy, +发放量, serverAuthoritative, reason="energy_purchase")。
///      已检空间,理论必成;极端并发(体力被其它路径顶高)失败 → 退钻(energy_purchase_refund)+ 回 OverLimit,不留「扣了钻没体力」;
///   ⑦ 双 delta 推送(钻 + 体力),让通用属性视图与购买响应不分叉;
///   ⑧ BuyEnergyUsedToday+1(原子 $inc)。
/// 每日门控 / OverLimit 在扣钻前返回,拒绝时不消耗次数(与看广告 ClaimAdEnergyHelper 对玩家友好口径一致)。
///
/// 反作弊红线:请求只表意图,单价 / 发放量 / 每日上限服务端说了算,走 serverAuthoritative 跳过客户端 RPC 路径单笔上限 / 频率闸。
/// 失败一律以 ResultCode 回包,不抛异常断连;时钟统一 TimeHelper.Now。
/// 设计基线:.claude/rules/data-authority.md(资源服务端权威、限界信任、每日限次=服务端闸 + 派生)。
/// </summary>
public static class BuyEnergyHelper
{
    /// <summary>
    /// 执行购买体力裁定。返回 (resultCode, diamond, energy, buyEnergyUsedToday):
    ///   - Success:diamond = 扣后钻石余额、energy = 发后体力余额、buyEnergyUsedToday = +1 后当日次数;
    ///   - NotEnoughDiamond:diamond = 当前钻石余额、energy = 当前体力(供 toast「需要 X,你有 Y」);
    ///   - OverLimit:energy = 当前体力(已满,买不进)、diamond = 当前钻石(未扣);
    ///   - DailyLimitReached:diamond / energy = 懒重置后当前权威值、buyEnergyUsedToday = 当日已用次数(= 上限);
    ///   - ServiceUnavailable:回带能读到的当前值(读失败为 0)。
    /// buyEnergyUsedToday 于成功 / 每日拒绝路径为服务端权威当日值;其它失败路径回带懒重置后读到的值(读失败为 0)。
    /// </summary>
    public static async FTask<(BuyEnergyResultCode resultCode, long diamond, long energy, int buyEnergyUsedToday)> TryBuy(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }

        var cost = BuyEnergyConfigServer.DiamondCost;
        var grant = BuyEnergyConfigServer.EnergyGrant;
        var nowMs = TimeHelper.Now;

        // ① 读 doc + 懒每日重置。未首登(理论上购买前必已登录,防御)→ 服务不可用。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"BuyEnergyHelper.TryBuy 读文档失败 account={accountId},err={e.Message}");
            return (BuyEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }
        if (doc == null)
        {
            Log.Warning($"BuyEnergyHelper.TryBuy:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (BuyEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }

        await ResetBuyEnergyIfDue(service, accountId, doc, nowMs);
        var usedToday = doc.BuyEnergyUsedToday;

        // ② 每日门控:重置后仍达上限 → 拒,不扣钻、不发、不消耗次数(回带懒重置后当前权威值)。
        if (usedToday >= BuyEnergyConfigServer.DailyLimit)
        {
            return (BuyEnergyResultCode.DailyLimitReached, doc.Diamond, doc.Energy, usedToday);
        }

        // ③ 读当前体力(已结算离线恢复)。读失败 → 服务不可用。
        var (okEnergy, curEnergy) = await PlayerPropertyServiceHelper.ReadEnergyAuthoritative(scene, accountId);
        if (!okEnergy)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, doc.Diamond, curEnergy, usedToday);
        }

        // ④ 检体力空间:加满发放量不得超存储硬上界(避免扣钻后发不进,产生「扣了钻没体力」)。
        if (curEnergy + grant > service.EnergyUpperBound)
        {
            return (BuyEnergyResultCode.OverLimit, doc.Diamond, curEnergy, usedToday);
        }

        // ⑤ 扣钻(服务端权威)。钻不足 / 服务不可用短路,不发体力、不消耗次数。
        var (chargeCode, newDiamond) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Diamond, -cost, "energy_purchase", serverAuthoritative: true);
        if (chargeCode == PropertyChangeResultCode.NotEnough)
        {
            return (BuyEnergyResultCode.NotEnoughDiamond, newDiamond, curEnergy, usedToday);
        }
        if (chargeCode != PropertyChangeResultCode.Success)
        {
            return (BuyEnergyResultCode.ServiceUnavailable, newDiamond, curEnergy, usedToday);
        }

        // ⑥ 发体力(服务端权威)。已检空间,理论必成;极端并发失败 → 退钻,回 OverLimit,不留半成品、不消耗次数。
        var (grantCode, newEnergy) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Energy, grant, "energy_purchase", serverAuthoritative: true);
        if (grantCode != PropertyChangeResultCode.Success)
        {
            var (refundCode, refundedDiamond) = await PlayerPropertyServiceHelper.ChangeProperty(
                scene, accountId, PropertyType.Diamond, cost, "energy_purchase_refund", serverAuthoritative: true);
            var diamondAfter = refundCode == PropertyChangeResultCode.Success ? refundedDiamond : newDiamond;
            Log.Warning($"BuyEnergyHelper:发体力失败 account={accountId} grantCode={grantCode},已退钻 refundCode={refundCode};回 OverLimit。");
            PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Diamond, diamondAfter, "energy_purchase_refund");
            return (BuyEnergyResultCode.OverLimit, diamondAfter, curEnergy, usedToday);
        }

        // ⑦ 双 delta 推送(钻 + 体力),避免通用属性视图与购买响应两条余额分叉。
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Diamond, newDiamond, "energy_purchase");
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Energy, newEnergy, "energy_purchase");

        // ⑧ 今日购买次数 +1(原子 $inc)。失败(体力/钻已成功)记 Warning、回带 +1 前的 usedToday,不回滚(沿 ledger 不回滚基线)。
        var incFilter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var incUpdate = Builders<PlayerDoc>.Update
            .Inc(x => x.BuyEnergyUsedToday, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);
        try
        {
            var updated = await players.FindOneAndUpdateAsync(incFilter, incUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (updated != null) usedToday = updated.BuyEnergyUsedToday;
        }
        catch (MongoException e)
        {
            Log.Warning($"BuyEnergyHelper.TryBuy 购买次数 +1 失败 account={accountId}(钻/体力已发),err={e.Message}");
        }

        Log.Info($"购买体力成功 account={accountId} cost={cost} grant={grant} diamond={newDiamond} energy={newEnergy} buyUsedToday={usedToday}/{BuyEnergyConfigServer.DailyLimit}");
        return (BuyEnergyResultCode.Success, newDiamond, newEnergy, usedToday);
    }

    /// <summary>
    /// 钻石购买体力每日懒重置:跨服务端本地日则 BuyEnergyUsedToday=0 + 刷新 BuyEnergyLastResetUnixMs=nowMs。
    /// 口径 / CAS / 旧档防御与 WishHelper.ResetWishIfDue / ClaimAdEnergyHelper.ResetAdEnergyIfDue 完全一致
    /// (服务端本地日期跨天判、CAS 防并发双重置、就地更新入参 doc、同日 no-op 不写库)。
    /// </summary>
    public static async FTask ResetBuyEnergyIfDue(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        var players = service.Players;
        if (players == null) return;

        var lastMs = doc.BuyEnergyLastResetUnixMs;

        // 同一服务端本地日历日 → 不重置(no-op,不写库)。
        var lastDate = lastMs.TransitionLocal().Date;
        var nowDate = nowMs.TransitionLocal().Date;
        if (lastMs > 0L && lastDate == nowDate)
        {
            return;
        }

        // 跨天(或旧档 lastMs<=0 防御)→ CAS 重置:次数归零 + 刷新重置时刻。
        var casFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.BuyEnergyLastResetUnixMs, lastMs));
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.BuyEnergyUsedToday, 0)
            .Set(x => x.BuyEnergyLastResetUnixMs, nowMs);
        try
        {
            var newDoc = await players.FindOneAndUpdateAsync(casFilter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (newDoc != null)
            {
                doc.BuyEnergyUsedToday = newDoc.BuyEnergyUsedToday;
                doc.BuyEnergyLastResetUnixMs = newDoc.BuyEnergyLastResetUnixMs;
                Log.Debug($"钻石购买体力每日重置 account={accountId} buyEnergyUsedToday={newDoc.BuyEnergyUsedToday} lastResetMs={newDoc.BuyEnergyLastResetUnixMs}");
            }
            // newDoc == null:并发已被另一路重置,本路当作已完成(沿 WishHelper 范式)。
        }
        catch (MongoException e)
        {
            Log.Warning($"BuyEnergyHelper.ResetBuyEnergyIfDue 重置失败 account={accountId},err={e.Message}");
        }
    }
}
