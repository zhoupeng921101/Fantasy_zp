using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 看广告领体力(每日限领)服务端权威裁决核心(体力系统:补广告获取源,桩流程)。
///
/// 每日次数闸 + 体力发一律服务端说了算(PlayerDoc.AdEnergyUsedToday / AdEnergyLastResetUnixMs)。
/// 请求只带身份(从会话取),发放量 / 每日上限由服务端按 AdEnergyConfigServer 派生,客户端不上报——反作弊红线。
/// 与祈愿(WishHelper)同范式,区别:无灵力消耗(广告是免费源),故拒绝路径少一个 NotEnoughSoul。
///
/// 处理顺序:
///   ① 懒每日重置(ResetAdEnergyIfDue):跨服务端本地日则 AdEnergyUsedToday=0 + 刷新 AdEnergyLastResetUnixMs(CAS 防并发双重置);
///   ② 门控:重置后 AdEnergyUsedToday >= DailyMax → DailyLimitReached,不发、不消耗次数;
///   ③ 检体力空间:读权威体力 E(已含懒恢复结算),E >= EnergyCap 软上限 → OverLimit,不发、不消耗次数
///      (广告免费源,满时拒绝且不吃次数——与祈愿「满时仍扣灵力吃次数」不同:祈愿玩家主动花灵力,广告是白给,满时白吃一次对玩家不友好);
///   ④ 发体力:netDelta = min(EnergyCap, E+grant) - E(③ 已挡满故必 > 0),ChangeProperty(Energy, +netDelta, "ad_reward");
///   ⑤ AdEnergyUsedToday+1(原子 $inc)。
///
/// 桩流程:本轮客户端点「看广告」直接发 RPC(无真实广告播放);后续接广告 SDK 只在客户端「播放完成回调」后再发本 RPC,服务端不变。
/// 失败一律以 ResultCode 回包,不抛异常断连;时钟统一 TimeHelper.Now。
/// 并发:同 WishHelper——当前 demo 每 UUID 只一个在线会话,同账号并发不可能;懒重置仍带 CAS 沿范式更稳。
/// 设计基线:.claude/rules/data-authority.md(每日限领资源=服务端闸 + 派生、限界信任、身份服务端)。
/// </summary>
public static class ClaimAdEnergyHelper
{
    /// <summary>
    /// 执行看广告领体力裁定。返回 (resultCode, energy, adEnergyUsedToday):
    ///   - Success:energy = 发后体力(夹软上限)、adEnergyUsedToday = +1 后当日次数;
    ///   - DailyLimitReached / OverLimit / ServiceUnavailable:回带懒重置后服务端当前权威值(便于客户端算剩余次数)。
    /// </summary>
    public static async FTask<(ClaimAdEnergyResultCode resultCode, long energy, int adEnergyUsedToday)> TryClaim(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (ClaimAdEnergyResultCode.ServiceUnavailable, 0L, 0);
        }

        var nowMs = TimeHelper.Now;

        // ① 读 doc + 懒每日重置。未首登(理论上领取前必已登录,防御)→ 服务不可用。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"ClaimAdEnergyHelper.TryClaim 读文档失败 account={accountId},err={e.Message}");
            return (ClaimAdEnergyResultCode.ServiceUnavailable, 0L, 0);
        }
        if (doc == null)
        {
            Log.Warning($"ClaimAdEnergyHelper.TryClaim:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (ClaimAdEnergyResultCode.ServiceUnavailable, 0L, 0);
        }

        await ResetAdEnergyIfDue(service, accountId, doc, nowMs);
        var usedToday = doc.AdEnergyUsedToday;
        var energy = doc.Energy;

        // ② 门控:重置后仍达上限 → 拒,不发、不消耗次数。
        if (usedToday >= AdEnergyConfigServer.DailyMax)
        {
            return (ClaimAdEnergyResultCode.DailyLimitReached, energy, usedToday);
        }

        // ③ 检体力空间:读权威体力(已跑懒恢复结算),已达软上限则拒(不发、不消耗次数)。
        var (energyOk, curEnergy) = await PlayerPropertyServiceHelper.ReadEnergyAuthoritative(scene, accountId);
        if (!energyOk)
        {
            Log.Warning($"ClaimAdEnergyHelper.TryClaim 读权威体力失败 account={accountId},返 ServiceUnavailable。");
            return (ClaimAdEnergyResultCode.ServiceUnavailable, energy, usedToday);
        }
        var cap = MergeOrderConfigServer.EnergyCap;
        if (curEnergy >= cap)
        {
            return (ClaimAdEnergyResultCode.OverLimit, curEnergy, usedToday);
        }

        // ④ 发体力(夹 EnergyCap 软上限)。③ 已挡满,netDelta 必 > 0。
        var targetEnergy = curEnergy + AdEnergyConfigServer.EnergyGrant;
        if (targetEnergy > cap) targetEnergy = cap;
        var netDelta = targetEnergy - curEnergy;

        var (energyCode, newEnergy) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, accountId, PropertyType.Energy, netDelta, "ad_reward", serverAuthoritative: true);
        if (energyCode != PropertyChangeResultCode.Success)
        {
            Log.Warning($"ClaimAdEnergyHelper.TryClaim 发体力失败 account={accountId} netDelta={netDelta} code={energyCode},返 ServiceUnavailable(次数未加)。");
            return (ClaimAdEnergyResultCode.ServiceUnavailable, curEnergy, usedToday);
        }
        PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Energy, newEnergy, "ad_reward");
        energy = newEnergy;

        // ⑤ 今日次数 +1(原子 $inc)。
        var incFilter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var incUpdate = Builders<PlayerDoc>.Update
            .Inc(x => x.AdEnergyUsedToday, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);
        try
        {
            var updated = await players.FindOneAndUpdateAsync(incFilter, incUpdate,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (updated == null)
            {
                Log.Warning($"ClaimAdEnergyHelper.TryClaim:计数 +1 时文档不存在 account={accountId}(体力已发),返 ServiceUnavailable。");
                return (ClaimAdEnergyResultCode.ServiceUnavailable, energy, usedToday);
            }
            usedToday = updated.AdEnergyUsedToday;
        }
        catch (MongoException e)
        {
            Log.Warning($"ClaimAdEnergyHelper.TryClaim 计数 +1 失败 account={accountId}(体力已发),err={e.Message}");
            return (ClaimAdEnergyResultCode.ServiceUnavailable, energy, usedToday);
        }

        Log.Info($"看广告领体力成功 account={accountId} energy={energy} adEnergyUsedToday={usedToday}/{AdEnergyConfigServer.DailyMax}");
        return (ClaimAdEnergyResultCode.Success, energy, usedToday);
    }

    /// <summary>
    /// 看广告领体力每日懒重置:跨服务端本地日则 AdEnergyUsedToday=0 + 刷新 AdEnergyLastResetUnixMs=nowMs。
    /// 口径 / CAS / 旧档防御与 WishHelper.ResetWishIfDue 完全一致(服务端本地日期跨天判、CAS 防并发双重置、就地更新入参 doc、同日 no-op 不写库)。
    /// </summary>
    public static async FTask ResetAdEnergyIfDue(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        var players = service.Players;
        if (players == null) return;

        var lastMs = doc.AdEnergyLastResetUnixMs;

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
            Builders<PlayerDoc>.Filter.Eq(x => x.AdEnergyLastResetUnixMs, lastMs));
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.AdEnergyUsedToday, 0)
            .Set(x => x.AdEnergyLastResetUnixMs, nowMs);
        try
        {
            var newDoc = await players.FindOneAndUpdateAsync(casFilter, update,
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (newDoc != null)
            {
                doc.AdEnergyUsedToday = newDoc.AdEnergyUsedToday;
                doc.AdEnergyLastResetUnixMs = newDoc.AdEnergyLastResetUnixMs;
                Log.Debug($"看广告领体力每日重置 account={accountId} adEnergyUsedToday={newDoc.AdEnergyUsedToday} lastResetMs={newDoc.AdEnergyLastResetUnixMs}");
            }
            // newDoc == null:并发已被另一路重置,本路当作已完成(沿 WishHelper 范式)。
        }
        catch (MongoException e)
        {
            Log.Warning($"ClaimAdEnergyHelper.ResetAdEnergyIfDue 重置失败 account={accountId},err={e.Message}");
        }
    }
}
