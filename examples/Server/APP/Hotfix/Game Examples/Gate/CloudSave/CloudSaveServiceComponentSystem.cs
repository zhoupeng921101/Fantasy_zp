using Fantasy.Async;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 云存档服务端组件初始化:绑定 player_cloud_save 集合句柄 + 装入 blob 大小上限配置。
/// 集合 player_cloud_save 首次写入(upsert)时由 MongoDB 自动创建;_id = playerId 主键天然唯一,不需额外索引
/// (本子单查询模式只有按 playerId 单点 get/upsert,无范围查询)。
/// MongoDB 不可达 → Warning + 不绑句柄,后续上传/下载 Handler 返 ServiceUnavailable(沿 35 / 37 / 31 同基线)。
/// </summary>
public sealed class CloudSaveServiceComponentAwakeSystem : AwakeSystem<CloudSaveServiceComponent>
{
    /// <summary>
    /// 单份 blob 字节数上限默认值:1 MB。
    /// 占位值:挡客户端滥用 + 远低于 MongoDB 16MB 文档硬上限留出 BSON 元字段空间。
    /// 真实容量按客户端实测存档体积(局内棋盘 + 全部解锁标志 + dynamicWeight)调参。
    /// </summary>
    private const int DefaultBlobMaxBytes = 1 * 1024 * 1024;

    protected override void Awake(CloudSaveServiceComponent self)
    {
        self.BlobMaxBytes = DefaultBlobMaxBytes;

        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建(沿 P1/P2 先例)。
        Init(self).Coroutine();
    }

    private static async FTask Init(CloudSaveServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达:上传/下载 Handler 检测 Snapshots == null 返 ServiceUnavailable。
            // 属预期环境条件,用 Warning 不用 Error(同 PlayerPropertyServiceComponent / RankServiceComponent 先例)。
            Log.Warning("CloudSaveServiceComponent: MongoDB 实例不可用,云存档上传/下载将返回 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            await FTask.CompletedTask;
            return;
        }

        self.Snapshots = mongoDatabase.GetCollection<CloudSaveDoc>("player_cloud_save");

        Log.Info($"CloudSaveServiceComponent 初始化完成,blob 上限={self.BlobMaxBytes}字节。");
        await FTask.CompletedTask;
    }
}

public sealed class CloudSaveServiceComponentDestroySystem : DestroySystem<CloudSaveServiceComponent>
{
    protected override void Destroy(CloudSaveServiceComponent self)
    {
        self.Snapshots = null;
    }
}
