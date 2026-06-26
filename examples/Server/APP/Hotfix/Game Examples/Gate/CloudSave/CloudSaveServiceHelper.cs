using System;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 云存档服务端操作 helper:封装"上传(原子条件 upsert + version 单调推进)"与"下载(按 playerId 单点 get)"。
/// 与货币/排行榜的字段级权威是两条不同通道——本 helper 不解析 blob、不做内部字段校验,只解决"携带 + 冲突解决"。
///
/// 上传的并发原子性:filter 含「_id == playerId AND Version &lt; 上传 version」严格小于条件 + $set + $setOnInsert + upsert,
/// 由 MongoDB 单条 FindOneAndUpdate 原子完成,无 check-then-act 竞态:
///   - 文档不存在 → 走 upsert 分支(setOnInsert 写 PlayerId,set 写 Version/Blob/LastUpdateUnixMs)。
///   - 存在且 Version &lt; 上传 version → 走 set 分支覆盖。
///   - 存在且 Version &gt;= 上传 version → 匹配失败、不写;helper 再单点 get 拿当前权威 blob 回 Stale。
/// 并发两次同 version 上传:严格小于 → 后到那次匹配失败、判 Stale(由客户端合并后以更高 version 重传)。
/// </summary>
public static class CloudSaveServiceHelper
{
    /// <summary>上传裁决:返回结果码 + 服务端当前 version + (仅 Stale 时回带)服务端当前 blob。</summary>
    public static async FTask<(CloudSaveUploadResultCode resultCode, long serverVersion, byte[]? serverBlob)>
        Upload(CloudSaveServiceComponent service, string playerId, long uploadVersion, byte[] blob)
    {
        if (service.Snapshots == null)
        {
            return (CloudSaveUploadResultCode.ServiceUnavailable, 0L, null);
        }

        // 参数非法预筛(版本号正整数、blob 非 null);InvalidRequest 不细分,由客户端结合上下文排查。
        if (string.IsNullOrEmpty(playerId) || uploadVersion <= 0L || blob == null)
        {
            return (CloudSaveUploadResultCode.InvalidRequest, 0L, null);
        }

        if (blob.Length > service.BlobMaxBytes)
        {
            Log.Warning($"CloudSave 上传 blob 超限 playerId={playerId} size={blob.Length} limit={service.BlobMaxBytes}");
            return (CloudSaveUploadResultCode.BlobTooLarge, 0L, null);
        }

        var nowMs = Fantasy.Helper.TimeHelper.Now;

        try
        {
            // 原子条件 upsert:filter 含严格 Version < uploadVersion;
            //   - 文档不存在:filter 中 Version 字段比较视作不成立,但 upsert 仍会建新文档(MongoDB 文档生效语义),
            //     setOnInsert 写 PlayerId。
            //   - 文档存在且 Version < uploadVersion:set 推进版本 + 覆盖 blob。
            //   - 文档存在且 Version >= uploadVersion:匹配失败,无操作。
            var filter = Builders<CloudSaveDoc>.Filter.And(
                Builders<CloudSaveDoc>.Filter.Eq(x => x.PlayerId, playerId),
                Builders<CloudSaveDoc>.Filter.Lt(x => x.Version, uploadVersion));
            var update = Builders<CloudSaveDoc>.Update
                .Set(x => x.Version, uploadVersion)
                .Set(x => x.Blob, blob)
                .Set(x => x.LastUpdateUnixMs, nowMs)
                .SetOnInsert(x => x.PlayerId, playerId);
            var options = new FindOneAndUpdateOptions<CloudSaveDoc>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var updated = await service.Snapshots.FindOneAndUpdateAsync(filter, update, options);
            if (updated != null && updated.Version == uploadVersion)
            {
                Log.Debug($"CloudSave 上传接受 playerId={playerId} version={uploadVersion} size={blob.Length}");
                return (CloudSaveUploadResultCode.Accepted, uploadVersion, null);
            }

            // 走到这里:filter 匹配失败(已存 Version >= uploadVersion),需读当前权威值回 Stale。
            // FindOneAndUpdate 在 upsert + filter 不匹配但 _id 已存在时会抛 DuplicateKey,catch 段统一兜底;
            // 但若 ReturnDocument.After 拿到的 doc.Version != uploadVersion(理论上只在并发同 version 才可能),
            // 也按 Stale 处理:重读当前权威值。
            var current = await ReadCurrent(service, playerId);
            return (CloudSaveUploadResultCode.Stale, current?.Version ?? 0L, current?.Blob);
        }
        catch (MongoWriteException mwe) when (mwe.WriteError != null && mwe.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // upsert + filter 不匹配但 _id 已存在 → MongoDB 抛重复键。这是"已存 version >= 上传"的并发表现。
            // C# 驱动会按场景把 duplicate-key 包成 MongoWriteException(单文档写)或 MongoCommandException(批量/命令通道),
            // 两种形态都按 Stale 处理(同 Rank/Activity/Mail/Redeem 双 catch 范式)。
            var current = await ReadCurrent(service, playerId);
            return (CloudSaveUploadResultCode.Stale, current?.Version ?? 0L, current?.Blob);
        }
        catch (MongoCommandException e) when (e.Code == 11000 /* DuplicateKey */)
        {
            var current = await ReadCurrent(service, playerId);
            return (CloudSaveUploadResultCode.Stale, current?.Version ?? 0L, current?.Blob);
        }
        catch (Exception e)
        {
            Log.Warning($"CloudSaveServiceHelper.Upload 失败 playerId={playerId} version={uploadVersion} err={e.Message}");
            return (CloudSaveUploadResultCode.ServiceUnavailable, 0L, null);
        }
    }

    /// <summary>下载裁决:返回结果码 + 服务端当前 version + blob(NoSnapshot / 失败时 blob 为空)。</summary>
    public static async FTask<(CloudSaveDownloadResultCode resultCode, long serverVersion, byte[] serverBlob)>
        Download(CloudSaveServiceComponent service, string playerId)
    {
        if (service.Snapshots == null)
        {
            return (CloudSaveDownloadResultCode.ServiceUnavailable, 0L, Array.Empty<byte>());
        }
        if (string.IsNullOrEmpty(playerId))
        {
            // 调用方应已挡掉空 playerId(从会话取);兜底以 NoSnapshot 回(空身份没有任何存档可读)。
            return (CloudSaveDownloadResultCode.NoSnapshot, 0L, Array.Empty<byte>());
        }

        try
        {
            var doc = await ReadCurrent(service, playerId);
            if (doc == null)
            {
                return (CloudSaveDownloadResultCode.NoSnapshot, 0L, Array.Empty<byte>());
            }
            return (CloudSaveDownloadResultCode.Success, doc.Version, doc.Blob ?? Array.Empty<byte>());
        }
        catch (Exception e)
        {
            Log.Warning($"CloudSaveServiceHelper.Download 失败 playerId={playerId} err={e.Message}");
            return (CloudSaveDownloadResultCode.ServiceUnavailable, 0L, Array.Empty<byte>());
        }
    }

    /// <summary>
    /// 清档·删除某 playerId 的云存档文档(_id=playerId 单条删除)。
    /// 下次登录 / 进入主游戏时 Download 返 NoSnapshot,客户端起空盘新局。
    /// 幂等:文档不存在(DeletedCount=0)同样视为成功(无档可删等价已是无档态)。
    /// 返回 true = 成功(含本就无档);false = MongoDB 不可达 / 异常。
    /// </summary>
    public static async FTask<bool> Delete(CloudSaveServiceComponent service, string playerId)
    {
        if (service.Snapshots == null)
        {
            return false;
        }
        if (string.IsNullOrEmpty(playerId))
        {
            // 空身份无任何存档可删,幂等视为成功。
            return true;
        }

        try
        {
            var filter = Builders<CloudSaveDoc>.Filter.Eq(x => x.PlayerId, playerId);
            var result = await service.Snapshots.DeleteOneAsync(filter);
            Log.Debug($"CloudSave 清档删除 playerId={playerId} deletedCount={result.DeletedCount}");
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"CloudSaveServiceHelper.Delete 失败 playerId={playerId} err={e.Message}");
            return false;
        }
    }

    private static async FTask<CloudSaveDoc?> ReadCurrent(CloudSaveServiceComponent service, string playerId)
    {
        if (service.Snapshots == null) return null;
        var filter = Builders<CloudSaveDoc>.Filter.Eq(x => x.PlayerId, playerId);
        return await service.Snapshots.Find(filter).FirstOrDefaultAsync();
    }
}
