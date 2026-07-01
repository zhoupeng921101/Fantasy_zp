using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 头像 / 头像框服务端权威裁决核心(原云存档 blob 退役·第 2 批·子批 2c)。
///
/// 两类对外能力(身份由调用方从会话取,本 helper 只吃 accountId,不接受客户端上报账号):
///   1. TryEquip — 换装:校验目标 id 已在对应已解锁集合内(未解锁拒)→ 单条原子 $set 当前佩戴 id。
///   2. TryUnlock — 解锁上报(client-report 限界信任):sanity(id 落合法段 + 集合大小上限 + 频率闸)→
///      单条原子 $addToSet 幂等加入集合(重复上报同 id 无副作用,MongoDB $addToSet 天然去重)。
///
/// 修饰种类(Kind)= 客户端 AvatarType(1=头像 / 2=头像框),据此选当前 id 字段 / 解锁集合字段 / id 合法段。
/// 校验强度:饰品低危,解锁走 client-report(不在服务端建整套头像等级配置自算);限界信任只挡异常
///   (越段 id / 灌爆集合 / spam 频率),不逐条核对「该 id 是否真达等级解锁条件」(设计决策)。
///
/// 失败一律以结果码回包,不抛异常断连;MongoDB 不可达 → ServiceUnavailable。时钟统一 TimeHelper.Now。
/// 设计基线:.claude/rules/data-authority.md(修饰服务端权威、解锁 client-report 限界信任、身份服务端)。
/// </summary>
public static class CosmeticHelper
{
    /// <summary>
    /// 换装。返回 (resultCode, currentAvatarId, currentFrameId):
    ///   - Success:当前佩戴 id 已切换,回带切换后两个当前 id;
    ///   - InvalidKind / NotUnlocked / ServiceUnavailable:回带服务端当前权威两个 id(读失败为 0,供客户端回退)。
    /// </summary>
    public static async FTask<(EquipCosmeticResultCode resultCode, int currentAvatarId, int currentFrameId)> TryEquip(
        Scene scene, string accountId, int kind, int id)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (EquipCosmeticResultCode.ServiceUnavailable, 0, 0);
        }

        if (kind != (int)CosmeticKind.Avatar && kind != (int)CosmeticKind.Frame)
        {
            var (ca0, cf0) = await ReadCurrentIds(players, accountId);
            return (EquipCosmeticResultCode.InvalidKind, ca0, cf0);
        }

        // 读 doc 拿当前解锁集合 + 当前佩戴 id。未首登(理论上换装前必已登录,防御)→ 服务不可用。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"CosmeticHelper.TryEquip 读文档失败 account={accountId},err={e.Message}");
            return (EquipCosmeticResultCode.ServiceUnavailable, 0, 0);
        }
        if (doc == null)
        {
            Log.Warning($"CosmeticHelper.TryEquip:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (EquipCosmeticResultCode.ServiceUnavailable, 0, 0);
        }

        var unlocked = kind == (int)CosmeticKind.Avatar ? doc.UnlockedAvatarIds : doc.UnlockedFrameIds;
        // 未解锁:目标 id 不在对应集合内 → 拒换,回带当前权威两个 id 供客户端回退。
        if (unlocked == null || !unlocked.Contains(id))
        {
            return (EquipCosmeticResultCode.NotUnlocked, doc.CurrentAvatarId, doc.CurrentFrameId);
        }

        // 已解锁 → 单条原子 $set 当前佩戴 id(对应种类字段)。
        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = kind == (int)CosmeticKind.Avatar
            ? Builders<PlayerDoc>.Update.Set(x => x.CurrentAvatarId, id).Set(x => x.LastChangeUnixMs, nowMs)
            : Builders<PlayerDoc>.Update.Set(x => x.CurrentFrameId, id).Set(x => x.LastChangeUnixMs, nowMs);
        var options = new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After };

        try
        {
            var updated = await players.FindOneAndUpdateAsync(filter, update, options);
            if (updated == null)
            {
                Log.Warning($"CosmeticHelper.TryEquip:写当前 id 时文档不存在 account={accountId} kind={kind} id={id},返 ServiceUnavailable。");
                return (EquipCosmeticResultCode.ServiceUnavailable, doc.CurrentAvatarId, doc.CurrentFrameId);
            }
            Log.Info($"换装成功 account={accountId} kind={kind} id={id} currentAvatar={updated.CurrentAvatarId} currentFrame={updated.CurrentFrameId}");
            return (EquipCosmeticResultCode.Success, updated.CurrentAvatarId, updated.CurrentFrameId);
        }
        catch (MongoException e)
        {
            Log.Warning($"CosmeticHelper.TryEquip 写当前 id 失败 account={accountId} kind={kind} id={id},err={e.Message}");
            return (EquipCosmeticResultCode.ServiceUnavailable, doc.CurrentAvatarId, doc.CurrentFrameId);
        }
    }

    /// <summary>
    /// 解锁上报(client-report 限界信任)。返回 (resultCode, unlockedIds):
    ///   - Success:id 已幂等加入对应集合,回带更新后集合;
    ///   - InvalidKind / InvalidId / SetFull / RateLimited / ServiceUnavailable:回带当前对应集合(读失败为空)。
    /// </summary>
    public static async FTask<(UnlockCosmeticResultCode resultCode, List<int> unlockedIds)> TryUnlock(
        Scene scene, string accountId, int kind, int id)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (UnlockCosmeticResultCode.ServiceUnavailable, new List<int>());
        }

        if (kind != (int)CosmeticKind.Avatar && kind != (int)CosmeticKind.Frame)
        {
            return (UnlockCosmeticResultCode.InvalidKind, new List<int>());
        }

        // 频率闸(限界信任·防脚本刷):同账号修饰操作最小间隔。单条 / 批量共用 IsUnlockRateLimited(批量对整批只检一次)。
        var nowMs = TimeHelper.Now;
        if (IsUnlockRateLimited(service, accountId, nowMs))
        {
            Log.Warning($"CosmeticHelper.TryUnlock 拒因=RateLimited account={accountId} kind={kind} id={id} min={service.CosmeticMinIntervalMs}ms");
            var cur = await ReadUnlockedSet(players, accountId, kind);
            return (UnlockCosmeticResultCode.RateLimited, cur);
        }

        // id 段 sanity + $addToSet 幂等写(单条 / 批量共用 AddUnlockedId,无频率闸)。
        return await AddUnlockedId(players, service, accountId, kind, id, nowMs);
    }

    /// <summary>
    /// 批量解锁上报:频率闸对整批**只检一次**(一次合法批操作),过闸后逐项 sanity(Kind 合法 + id 段 + 集合上限)
    /// + $addToSet 幂等;逐项 sanity 失败**跳过不阻断整批**。返回 (整体结果码, 头像集, 框集):
    ///   - Success:处理完回带两个 kind 的最终解锁集(客户端投影以此覆盖);
    ///   - RateLimited:被闸挡,回带当前两集合(客户端下次边界重试);
    ///   - ServiceUnavailable:服务不可用 / 读集合失败。
    /// 空 items = Success + 当前两集合(no-op)。逐项 $addToSet 各自原子(N 次写,幂等),不合并 $each ——
    /// 保留单条同款集合上限 filter 判定(逐项精确挡 SetFull),不引入先读后写竞态。
    /// </summary>
    public static async FTask<(UnlockCosmeticResultCode resultCode, List<int> avatarIds, List<int> frameIds)> TryUnlockBatch(
        Scene scene, string accountId, IReadOnlyList<(int kind, int id)> items)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (UnlockCosmeticResultCode.ServiceUnavailable, new List<int>(), new List<int>());
        }

        var nowMs = TimeHelper.Now;
        if (IsUnlockRateLimited(service, accountId, nowMs))
        {
            Log.Warning($"CosmeticHelper.TryUnlockBatch 拒因=RateLimited account={accountId} count={items?.Count ?? 0} min={service.CosmeticMinIntervalMs}ms");
            var (a, f) = await ReadBothSets(players, accountId);
            return (UnlockCosmeticResultCode.RateLimited, a, f);
        }

        if (items != null)
        {
            foreach (var (kind, id) in items)
            {
                if (kind != (int)CosmeticKind.Avatar && kind != (int)CosmeticKind.Frame)
                {
                    continue; // 非法 Kind:跳过该项,不阻断整批
                }
                // 逐项 sanity(id 段 / 集合上限)+ $addToSet 幂等;逐项结果忽略(最终以两集合为准),失败项不阻断。
                await AddUnlockedId(players, service, accountId, kind, id, nowMs);
            }
        }

        var (avatar, frame) = await ReadBothSets(players, accountId);
        return (UnlockCosmeticResultCode.Success, avatar, frame);
    }

    /// <summary>频率闸判定:被闸挡返 true;未挡则置位记录时刻返 false。单条 / 批量共用(批量对整批只调一次)。</summary>
    private static bool IsUnlockRateLimited(PlayerPropertyServiceComponent service, string accountId, long nowMs)
    {
        if (service.CosmeticMinIntervalMs <= 0L) return false;
        var rateKey = string.Concat(accountId, "|unlock");
        if (service.LastCosmeticAtMs.TryGetValue(rateKey, out var prevMs) &&
            nowMs - prevMs < service.CosmeticMinIntervalMs)
        {
            return true;
        }
        service.LastCosmeticAtMs[rateKey] = nowMs;
        return false;
    }

    /// <summary>
    /// 单 id 的 sanity(id 落合法段)+ $addToSet(幂等 + 集合上限)写,**无频率闸**(调用方管闸)、Kind 已由调用方校验合法。
    /// 集合大小上限用 filter 的 $expr 在原子命令内判定(避免先读后写竞态);$addToSet 天然去重保证幂等。
    /// 返回 (resultCode, 该 kind 更新后 / 当前集合):Success / InvalidId / SetFull / ServiceUnavailable。
    /// </summary>
    private static async FTask<(UnlockCosmeticResultCode resultCode, List<int> unlockedIds)> AddUnlockedId(
        IMongoCollection<PlayerDoc> players, PlayerPropertyServiceComponent service, string accountId, int kind, int id, long nowMs)
    {
        // sanity:id 落在对应合法段内。段边界靠 Kind 选(头像 [MinAvatarId,MaxAvatarId] / 框 [MinFrameId,MaxFrameId])。
        var (minId, maxId) = kind == (int)CosmeticKind.Avatar
            ? (service.MinAvatarId, service.MaxAvatarId)
            : (service.MinFrameId, service.MaxFrameId);
        if (id < minId || id > maxId)
        {
            Log.Warning($"CosmeticHelper.AddUnlockedId 拒因=InvalidId account={accountId} kind={kind} id={id} 段=[{minId},{maxId}]");
            var cur = await ReadUnlockedSet(players, accountId, kind);
            return (UnlockCosmeticResultCode.InvalidId, cur);
        }

        // 集合大小上限(防灌爆):$addToSet 幂等,重复上报同 id 不增大小,故仅当 id 尚不在集合内且已达上限才拒。
        // filter 复合:_id 锚 + 「集合大小 < 上限 或 集合已含该 id」→ 满足其一即允许写(已含时 addToSet 无副作用)。
        // MongoDB $addToSet 天然去重,幂等由其保证;大小上限用 filter 的 $expr 在原子命令内判定,避免先读后写竞态。
        var setFieldName = kind == (int)CosmeticKind.Avatar
            ? nameof(PlayerDoc.UnlockedAvatarIds)
            : nameof(PlayerDoc.UnlockedFrameIds);

        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Or(
                // 集合已含该 id → 允许(addToSet 无副作用,幂等重报);
                Builders<PlayerDoc>.Filter.AnyEq<int>(setFieldName, id),
                // 或集合大小 < 上限 → 允许新增(用 $expr 比大小,$size 依赖字段 present,旧档已由登录迁移补空数组)。
                new BsonDocumentFilterDefinition<PlayerDoc>(new MongoDB.Bson.BsonDocument("$expr",
                    new MongoDB.Bson.BsonDocument("$lt", new MongoDB.Bson.BsonArray
                    {
                        new MongoDB.Bson.BsonDocument("$size", new MongoDB.Bson.BsonDocument("$ifNull",
                            new MongoDB.Bson.BsonArray { "$" + setFieldName, new MongoDB.Bson.BsonArray() })),
                        service.UnlockedSetMaxSize
                    })))));

        var update = Builders<PlayerDoc>.Update
            .AddToSet<int>(setFieldName, id)
            .Set(x => x.LastChangeUnixMs, nowMs);
        var options = new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After };

        try
        {
            var updated = await players.FindOneAndUpdateAsync(filter, update, options);
            if (updated != null)
            {
                var set = kind == (int)CosmeticKind.Avatar ? updated.UnlockedAvatarIds : updated.UnlockedFrameIds;
                Log.Info($"解锁上报成功 account={accountId} kind={kind} id={id} setSize={(set?.Count ?? 0)}");
                return (UnlockCosmeticResultCode.Success, set ?? new List<int>());
            }

            // filter 未命中:账号不存在(未首登)或集合已满且 id 未含。区分两者以给准确结果码。
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null)
            {
                Log.Warning($"CosmeticHelper.AddUnlockedId:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
                return (UnlockCosmeticResultCode.ServiceUnavailable, new List<int>());
            }
            var curSet = kind == (int)CosmeticKind.Avatar ? doc.UnlockedAvatarIds : doc.UnlockedFrameIds;
            // 走到这里 = 集合已满且 id 未含(若 id 已含则 filter 的 AnyEq 分支必命中,不会到此)。
            Log.Warning($"CosmeticHelper.AddUnlockedId 拒因=SetFull account={accountId} kind={kind} id={id} setSize={(curSet?.Count ?? 0)} max={service.UnlockedSetMaxSize}");
            return (UnlockCosmeticResultCode.SetFull, curSet ?? new List<int>());
        }
        catch (MongoException e)
        {
            Log.Warning($"CosmeticHelper.AddUnlockedId 写集合失败 account={accountId} kind={kind} id={id},err={e.Message}");
            return (UnlockCosmeticResultCode.ServiceUnavailable, new List<int>());
        }
    }

    /// <summary>读两个 kind 的当前解锁集(失败或无档返两空表)。供批量解锁回带 / 被闸挡回带。</summary>
    private static async FTask<(List<int> avatarIds, List<int> frameIds)> ReadBothSets(
        IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return (new List<int>(), new List<int>());
            return (doc.UnlockedAvatarIds ?? new List<int>(), doc.UnlockedFrameIds ?? new List<int>());
        }
        catch (MongoException)
        {
            return (new List<int>(), new List<int>());
        }
    }

    /// <summary>读当前权威两个佩戴 id(失败或无档返 0/0)。仅供换装失败分支回带客户端回退显示。</summary>
    private static async FTask<(int currentAvatarId, int currentFrameId)> ReadCurrentIds(
        IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return (0, 0);
            return (doc.CurrentAvatarId, doc.CurrentFrameId);
        }
        catch (MongoException)
        {
            return (0, 0);
        }
    }

    /// <summary>读某种类当前已解锁集合(失败或无档返空)。仅供解锁上报失败分支回带客户端对账。</summary>
    private static async FTask<List<int>> ReadUnlockedSet(
        IMongoCollection<PlayerDoc> players, string accountId, int kind)
    {
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return new List<int>();
            var set = kind == (int)CosmeticKind.Avatar ? doc.UnlockedAvatarIds : doc.UnlockedFrameIds;
            return set ?? new List<int>();
        }
        catch (MongoException)
        {
            return new List<int>();
        }
    }
}
