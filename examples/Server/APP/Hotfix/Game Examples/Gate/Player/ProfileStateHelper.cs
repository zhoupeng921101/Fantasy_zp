using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 皮肤态 / 神庙装饰标志服务端权威裁决核心(原云存档 blob 退役·第 3 批·子批 3b)。
///
/// 三个「设置状态」(SET 语义,非累加计数)一次性全量上报:
///   SkinMono        —— 是否单色皮肤模式(0=彩色 / 1=单色)。
///   SkinMonoId      —— 当前单色皮肤 id(彩色态客户端记 -1=Unselected;单色态为在用 sprite 编号)。
///   TempleDecorated —— 已装饰厅数标量(前缀语义,= 已修厅数)。
///
/// client-report 限界信任(皮肤 / 装饰纯装饰、低危):sanity 校验通过后单条原子 $set 到 PlayerDoc,不建服务端配置自算。
///   sanity:SkinMono ∈ {0,1};SkinMonoId 为哨兵 -1(彩色态)或落 [SkinMonoIdMin, SkinMonoIdMax] 段内;
///          TempleDecorated ∈ [0, TempleDecoratedMax]。任一越界 → InvalidRequest 拒,不写库。
///
/// 幂等:重复 $set 同值无副作用(SET 覆盖语义)。身份由调用方从会话取,本 helper 只吃 accountId,不接受客户端上报账号。
/// 失败一律以结果码回包,不抛异常断连;MongoDB 不可达 → ServiceUnavailable。时钟统一 TimeHelper.Now。
/// 设计基线:.claude/rules/data-authority.md(装饰服务端权威、client-report 限界信任、身份服务端)。
/// </summary>
public static class ProfileStateHelper
{
    /// <summary>
    /// 设置三态。返回 (resultCode, skinMono, skinMonoId, templeDecorated):
    ///   - Success:三态已原子 $set,回带 set 后当前值;
    ///   - InvalidRequest / ServiceUnavailable:回带服务端当前权威三态(读失败为默认 0/-1/0,供客户端回退)。
    /// </summary>
    public static async FTask<(SetProfileStateResultCode resultCode, int skinMono, int skinMonoId, long templeDecorated)> TrySet(
        Scene scene, string accountId, int skinMono, int skinMonoId, long templeDecorated)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (SetProfileStateResultCode.ServiceUnavailable, 0, -1, 0L);
        }

        // sanity(限界信任):SkinMono ∈ {0,1};SkinMonoId 为哨兵 -1 或落合法段;TempleDecorated ∈ [0, 上界]。
        // 越界一律拒 + 回带当前权威三态供客户端回退,不写库。
        if (!IsValid(service, skinMono, skinMonoId, templeDecorated))
        {
            Log.Warning($"ProfileStateHelper.TrySet 拒因=InvalidRequest account={accountId} skinMono={skinMono} skinMonoId={skinMonoId} templeDecorated={templeDecorated} " +
                        $"(段:skinMonoId=[{service.SkinMonoIdMin},{service.SkinMonoIdMax}] 或 -1;templeDecorated=[0,{service.TempleDecoratedMax}])。");
            var (cm, ci, ct) = await ReadCurrent(players, accountId);
            return (SetProfileStateResultCode.InvalidRequest, cm, ci, ct);
        }

        // 单条原子 $set 三态(SET 覆盖语义,幂等)。IsUpsert=false:未首登(理论上上报前必已登录,防御)→ 匹配失败 → ServiceUnavailable。
        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.SkinMono, skinMono)
            .Set(x => x.SkinMonoId, skinMonoId)
            .Set(x => x.TempleDecorated, templeDecorated)
            .Set(x => x.LastChangeUnixMs, nowMs);
        var options = new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After };

        try
        {
            var updated = await players.FindOneAndUpdateAsync(filter, update, options);
            if (updated == null)
            {
                Log.Warning($"ProfileStateHelper.TrySet:写三态时文档不存在 account={accountId}(未首登?),返 ServiceUnavailable。");
                return (SetProfileStateResultCode.ServiceUnavailable, 0, -1, 0L);
            }
            Log.Info($"设置档案状态成功 account={accountId} skinMono={updated.SkinMono} skinMonoId={updated.SkinMonoId} templeDecorated={updated.TempleDecorated}");
            return (SetProfileStateResultCode.Success, updated.SkinMono, updated.SkinMonoId, updated.TempleDecorated);
        }
        catch (MongoException e)
        {
            Log.Warning($"ProfileStateHelper.TrySet 写三态失败 account={accountId},err={e.Message}");
            var (cm, ci, ct) = await ReadCurrent(players, accountId);
            return (SetProfileStateResultCode.ServiceUnavailable, cm, ci, ct);
        }
    }

    /// <summary>sanity 判定(限界信任):SkinMono ∈ {0,1};SkinMonoId 为哨兵 -1 或落合法段;TempleDecorated ∈ [0, 上界]。</summary>
    private static bool IsValid(PlayerPropertyServiceComponent service, int skinMono, int skinMonoId, long templeDecorated)
    {
        if (skinMono != 0 && skinMono != 1) return false;
        // -1 = 彩色态哨兵(Unselected),单独放行;否则须落合法皮肤 id 段。
        if (skinMonoId != -1 && (skinMonoId < service.SkinMonoIdMin || skinMonoId > service.SkinMonoIdMax)) return false;
        if (templeDecorated < 0L || templeDecorated > service.TempleDecoratedMax) return false;
        return true;
    }

    /// <summary>读当前权威三态(失败或无档返默认 0/-1/0)。仅供失败分支回带客户端回退。</summary>
    private static async FTask<(int skinMono, int skinMonoId, long templeDecorated)> ReadCurrent(
        IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return (0, -1, 0L);
            return (doc.SkinMono, doc.SkinMonoId, doc.TempleDecorated);
        }
        catch (MongoException)
        {
            return (0, -1, 0L);
        }
    }
}
