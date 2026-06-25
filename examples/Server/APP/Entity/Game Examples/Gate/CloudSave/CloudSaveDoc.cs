using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 云存档服务端 blob 文档(原生 MongoDB 文档,非框架 Entity)。
// 集合 player_cloud_save;_id = PlayerId(P0 签发的账号级权威身份,32 位小写 hex)。
// 服务端不解析 Blob 内部结构,只按 version 单调推进做冲突裁决:
//   filter:_id == playerId AND Version < 上传 version
//   update:$set(Version, Blob, LastUpdateUnixMs) + $setOnInsert(PlayerId)
//   options:upsert
// 这一条原子 FindOneAndUpdate 同时覆盖了「首次写入」与「严格更高版本覆盖」,
// 并发两次上传只有更高 version 的能写进(并列 version 在同一并发窗口下后到者也会判 Stale,
// 即"上传 version > 已存"严格大于,SV 防同 version 重写)。
// 设计意图:云存档只解决"携带"(portability),不是权威校验;与货币/排行榜的字段级权威是两条不同通道。

/// <summary>
/// 云存档文档:每 playerId 一行,记当前权威版本 + 字节快照 + 末次更新时刻。
/// </summary>
public sealed class CloudSaveDoc
{
    /// <summary>玩家身份 id(= PlayerDoc.PlayerId,32 位小写 hex),作为 _id 主键(按 playerId 隔离,A 读不到 B)。</summary>
    [BsonId]
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>
    /// 当前权威版本号(单调递增)。客户端上传 version 必须 &gt; 此值才接受;
    /// &lt;= 此值返 Stale + 回带当前权威值供客户端合并重传。
    /// 默认 0 = 文档不存在(首次上传走 upsert 分支)。
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// 客户端序列化好的存档字节流(opaque blob,服务端不解析)。
    /// 上限由 CloudSaveServiceComponent.BlobMaxBytes 控制,超限上传被拒(防滥用 + 防超 BSON 16MB 文档限)。
    /// </summary>
    public byte[] Blob { get; set; } = System.Array.Empty<byte>();

    /// <summary>末次更新服务端时刻(Unix 毫秒, UTC),供排查 / 审计。每次写入成功后刷新。</summary>
    public long LastUpdateUnixMs { get; set; }
}
