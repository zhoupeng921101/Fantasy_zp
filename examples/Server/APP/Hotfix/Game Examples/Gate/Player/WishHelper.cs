using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 祈愿(每日限领体力)服务端权威裁决核心(原云存档 blob 退役·第 3 批·子批 3a)。
///
/// 每日祈愿次数闸 + 灵力扣 + 体力发一律服务端说了算(PlayerDoc.WishUsedToday / WishLastResetUnixMs)。
/// 请求只带身份(从会话取),灵力费 / 体力发 / 每日上限由服务端按 WishConfigServer 派生,客户端不上报——反作弊红线。
///
/// 处理顺序(与客户端 MergeOrderState.WishForEnergy 同判定次序,一律服务端权威):
///   ① 懒每日重置(ResetWishIfDue):按服务端本地日期判跨天,跨天则 WishUsedToday=0 + 刷新 WishLastResetUnixMs(CAS 防并发双重置);
///   ② 门控:重置后 WishUsedToday >= WishDailyLimit → DailyLimitReached,不扣不发;
///   ③ 扣灵力:ChangeProperty(SoulPower, -WishSoulCost, serverAuthoritative:true, reason="wish");
///      灵力不足(NotEnough)→ NotEnoughSoul、不发体力;服务不可用同理短路;
///   ④ 发体力:ChangeProperty(Energy, +gain, serverAuthoritative:true, reason="wish"),服务端夹 EnergyCap 软上限
///      (复刻客户端 Math.Min(EnergyCap, Energy+gain)):先读权威体力 E(已含懒恢复结算),
///      实发 = min(EnergyCap, E+gain) - E(≤ 0 则不发,体力已到/超软上限时祈愿只扣灵力不涨体力,与客户端夹 cap 语义一致);
///   ⑤ WishUsedToday+1(原子 $inc)。
///
/// 体力夹 cap 与落子派生口径协调:发体力前 ReadEnergyAuthoritative 先跑 RecoverEnergyIfDue(同 nowMs 懒恢复),
/// 随后 ChangeProperty(Energy) 内部再次 RecoverEnergyIfDue 因 elapsed &lt; interval 变 no-op(时间闸整 tick 前进),
/// 故 $inc(netDelta) 落在读到的 E 上、命中算定目标体力,与落子派生同一「读权威 E → 算 netDelta → ChangeProperty」范式。
///
/// 并发:当前 demo 每 UUID 只一个在线会话(AccountManageComponentSystem.Add 同 UUID 二次登录返 false),
/// 同账号并发祈愿实际不可能;懒重置仍带 CAS(WishLastResetUnixMs==读值)沿 RecoverEnergyIfDue 范式更稳。
///
/// 失败一律以 ResultCode 回包,不抛异常断连;时钟统一 TimeHelper.Now。
/// 设计基线:.claude/rules/data-authority.md(每日限领资源=服务端闸 + 派生、限界信任、身份服务端)。
/// </summary>
public static class WishHelper
{
    /// <summary>
    /// 执行祈愿裁定。返回 (resultCode, soulPower, energy, wishUsedToday):
    ///   - Success:soulPower = 扣后灵力、energy = 发后体力(夹软上限)、wishUsedToday = +1 后当日次数;
    ///   - DailyLimitReached / NotEnoughSoul / ServiceUnavailable:回带懒重置后服务端当前权威值(便于客户端回退 / 算剩余次数)。
    /// </summary>
    public static async FTask<(WishForEnergyResultCode resultCode, long soulPower, long energy, int wishUsedToday)> TryWish(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (WishForEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }

        var nowMs = TimeHelper.Now;

        // ① 读 doc + 懒每日重置。未首登(理论上祈愿前必已登录,防御)→ 服务不可用。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"WishHelper.TryWish 读文档失败 account={accountId},err={e.Message}");
            return (WishForEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }
        if (doc == null)
        {
            Log.Warning($"WishHelper.TryWish:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (WishForEnergyResultCode.ServiceUnavailable, 0L, 0L, 0);
        }

        // 懒重置(会就地更新 doc.WishUsedToday / WishLastResetUnixMs 为重置后值)。
        await ResetWishIfDue(service, accountId, doc, nowMs);
        var usedToday = doc.WishUsedToday;
        var soul = doc.SoulPower;
        var energy = doc.Energy;

        // ② 门控:重置后仍达上限 → 拒,不扣不发。
        if (usedToday >= WishConfigServer.WishDailyLimit)
        {
            return (WishForEnergyResultCode.DailyLimitReached, soul, energy, usedToday);
        }

        // ③ 扣灵力(服务端权威派生)。灵力不足 / 服务不可用短路,不发体力、不加次数。
        var (soulCode, newSoul) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.SoulPower, -WishConfigServer.WishSoulCost, "wish", serverAuthoritative: true);
        if (soulCode == PropertyChangeResultCode.NotEnough)
        {
            // 灵力不足:newSoul = 当前实际余额(ChangeProperty 匹配失败时回带)。体力未动。
            return (WishForEnergyResultCode.NotEnoughSoul, newSoul, energy, usedToday);
        }
        if (soulCode != PropertyChangeResultCode.Success)
        {
            return (WishForEnergyResultCode.ServiceUnavailable, soul, energy, usedToday);
        }
        // 扣灵力成功:显式起 SoulPower delta 推送(同 RenameHelper 范式,让所有灵力消费者收到权威新余额,避免分叉)。
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.SoulPower, newSoul, "wish");
        soul = newSoul;

        // ④ 发体力(夹 EnergyCap 软上限,复刻客户端 Math.Min(EnergyCap, Energy+gain))。
        // 先读权威体力 E(内部跑 RecoverEnergyIfDue 懒恢复,与落子派生同源);
        // 实发 netDelta = min(cap, E+gain) - E:E 已到/超 cap 时 netDelta <= 0,不发体力(祈愿只扣灵力,与客户端夹 cap 语义一致)。
        var (energyOk, curEnergy) = await PlayerPropertyServiceHelper.ReadEnergyAuthoritative(scene, accountId);
        if (!energyOk)
        {
            // 体力读失败:灵力已扣、次数未加。记 Warning(此时灵力已消耗但体力未发,下次祈愿正常;不回滚灵力,沿 ledger 不回滚基线)。
            Log.Warning($"WishHelper.TryWish 发体力前读权威体力失败 account={accountId}(灵力已扣 {WishConfigServer.WishSoulCost}),返 ServiceUnavailable。");
            return (WishForEnergyResultCode.ServiceUnavailable, soul, energy, usedToday);
        }

        var cap = MergeOrderConfigServer.EnergyCap;
        var targetEnergy = curEnergy + WishConfigServer.WishEnergyGain;
        if (targetEnergy > cap) targetEnergy = cap;
        var energyNetDelta = targetEnergy - curEnergy;

        energy = curEnergy;
        if (energyNetDelta > 0)
        {
            var (energyCode, newEnergy) = await PlayerPropertyServiceHelper.ChangeProperty(
                scene, accountId, PropertyType.Energy, energyNetDelta, "wish", serverAuthoritative: true);
            if (energyCode != PropertyChangeResultCode.Success)
            {
                // 体力发放失败:灵力已扣、次数未加。记 Warning、返 ServiceUnavailable(不回滚灵力)。
                Log.Warning($"WishHelper.TryWish 发体力失败 account={accountId} netDelta={energyNetDelta} code={energyCode}(灵力已扣),返 ServiceUnavailable。");
                return (WishForEnergyResultCode.ServiceUnavailable, soul, energy, usedToday);
            }
            PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Energy, newEnergy, "wish");
            energy = newEnergy;
        }
        // energyNetDelta <= 0:体力已到/超软上限,祈愿不涨体力(灵力仍扣、次数仍 +1),energy 保持读到的当前值。

        // ⑤ 今日次数 +1(原子 $inc)。
        var incFilter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var incUpdate = Builders<PlayerDoc>.Update
            .Inc(x => x.WishUsedToday, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);
        try
        {
            var updated = await players.FindOneAndUpdateAsync(incFilter, incUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (updated == null)
            {
                Log.Warning($"WishHelper.TryWish:计数 +1 时文档不存在 account={accountId}(灵力/体力已发),返 ServiceUnavailable。");
                return (WishForEnergyResultCode.ServiceUnavailable, soul, energy, usedToday);
            }
            usedToday = updated.WishUsedToday;
        }
        catch (MongoException e)
        {
            Log.Warning($"WishHelper.TryWish 计数 +1 失败 account={accountId}(灵力/体力已发),err={e.Message}");
            return (WishForEnergyResultCode.ServiceUnavailable, soul, energy, usedToday);
        }

        Log.Info($"祈愿成功 account={accountId} soul={soul} energy={energy} wishUsedToday={usedToday}/{WishConfigServer.WishDailyLimit}");
        return (WishForEnergyResultCode.Success, soul, energy, usedToday);
    }

    /// <summary>
    /// 祈愿每日懒重置:按服务端本地日期判 WishLastResetUnixMs 与 nowMs 是否跨天,跨天则 WishUsedToday=0 + 刷新 WishLastResetUnixMs=nowMs。
    ///
    /// 跨天判据用服务端本地日期(TimeHelper 的 TransitionLocal().Date),与客户端 MergeMetaPersistence.ApplyDailyReset 的本地日期口径对齐。
    /// CAS:filter 含 WishLastResetUnixMs == 读到值防并发双重置(沿 RecoverEnergyIfDue 范式)。
    /// 就地更新入参 doc(重置成功后 doc.WishUsedToday=0 / doc.WishLastResetUnixMs=nowMs);同日 no-op、不写库。
    /// 旧档 WishLastResetUnixMs==0 由 InitOrLoad 补字段迁移已补为 nowMs;防御性:若仍为 0(TransitionLocal→1970 年)则必判跨天、重置(收敛安全)。
    /// 失败(MongoDB 不可达 / 抖动)→ Warning 不抛、不阻断后续(入参 doc 保留读到的值)。
    /// </summary>
    public static async FTask ResetWishIfDue(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        var players = service.Players;
        if (players == null) return;

        var lastMs = doc.WishLastResetUnixMs;

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
            Builders<PlayerDoc>.Filter.Eq(x => x.WishLastResetUnixMs, lastMs));
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.WishUsedToday, 0)
            .Set(x => x.WishLastResetUnixMs, nowMs);
        try
        {
            var newDoc = await players.FindOneAndUpdateAsync(casFilter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (newDoc != null)
            {
                doc.WishUsedToday = newDoc.WishUsedToday;
                doc.WishLastResetUnixMs = newDoc.WishLastResetUnixMs;
                Log.Debug($"祈愿每日重置 account={accountId} wishUsedToday={newDoc.WishUsedToday} lastResetMs={newDoc.WishLastResetUnixMs}");
            }
            // newDoc == null:并发已被另一路重置,本路当作已完成(入参 doc 仍是读到值,调用方紧接门控;
            // 罕见并发下最坏是本次门控用旧 usedToday,不影响原子扣发正确性——扣发仍走独立原子命令)。
        }
        catch (MongoException e)
        {
            Log.Warning($"WishHelper.ResetWishIfDue 重置失败 account={accountId},err={e.Message}");
        }
    }
}
