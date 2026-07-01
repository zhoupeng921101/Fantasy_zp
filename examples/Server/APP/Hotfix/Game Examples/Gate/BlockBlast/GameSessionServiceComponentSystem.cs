using Fantasy.Async;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// Block Blast 对局持久服务端组件初始化:绑定 block_blast_session 集合句柄。
/// 集合首次写入(upsert)时由 MongoDB 自动创建;_id = playerId 主键天然唯一,无范围查询、不需额外索引。
/// MongoDB 不可达 → Warning + 不绑句柄,后续存盘静默跳过、读盘返 null(等价新建对局),不阻断玩法。
/// </summary>
public sealed class GameSessionServiceComponentAwakeSystem : AwakeSystem<GameSessionServiceComponent>
{
    protected override void Awake(GameSessionServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    private static async FTask Init(GameSessionServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            Log.Warning("GameSessionServiceComponent: MongoDB 实例不可用,Block Blast 对局续存将不可用(每次开窗新建)。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            await FTask.CompletedTask;
            return;
        }

        self.Sessions = mongoDatabase.GetCollection<GameSessionDoc>("block_blast_session");

        Log.Info("GameSessionServiceComponent 初始化完成(block_blast_session)。");
        await FTask.CompletedTask;
    }
}

public sealed class GameSessionServiceComponentDestroySystem : DestroySystem<GameSessionServiceComponent>
{
    protected override void Destroy(GameSessionServiceComponent self)
    {
        self.Sessions = null;
    }
}
