using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 云存档(portability)服务端组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持有 player_cloud_save 集合句柄 + blob 大小上限配置;
/// 上传/下载 Handler 经此组件做原子条件 upsert(version 单调推进)。
/// 寻址按 playerId(P0 身份锚);与货币/排行榜的字段级权威是两条不同通道——本组件不解析 blob、不做字段校验。
/// </summary>
public sealed class CloudSaveServiceComponent : Entity
{
    /// <summary>云存档集合(player_cloud_save),_id = playerId 字符串。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<CloudSaveDoc>? Snapshots;

    /// <summary>
    /// 单份 blob 字节数上限。超过此值的上传被拒(Result=BlobTooLarge),并 Log.Warning。
    /// 设计意图:挡客户端滥用 / 防超 MongoDB 文档 16MB 硬上限;
    /// 同时压住运营成本(单玩家 1MB × 玩家数 已是可观存储)。占位值 1MB,真实容量按客户端实测存档体积调参。
    /// </summary>
    public int BlobMaxBytes;
}
