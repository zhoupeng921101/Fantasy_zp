using Fantasy.Async;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 账号账本服务端组件初始化:绑定原生 MongoDB 集合句柄。
/// 集合 accounts 首次 upsert 时由 MongoDB 自动创建(同邮件 mail_template 等先例);_id 主键天然唯一,
/// 不另建索引(本子单无按时间查询需求,Tier 1+ 真要再加,SV2)。
/// 设计基线:design-docs/35-account-server.md §3.1 + §3.2。
/// </summary>
public sealed class AccountServiceComponentAwakeSystem : AwakeSystem<AccountServiceComponent>
{
    protected override void Awake(AccountServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    private static async FTask Init(AccountServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):账号 upsert 无法持久,登录会返登录失败错误码。
            // 这对应交接区 BLOCKED-环境:逻辑就绪、运行依赖外部 MongoDB。属预期环境条件,用 Warning 不用 Error。
            Log.Warning("AccountServiceComponent: MongoDB 实例不可用,首连自动注册 / 重连刷新末次登录将失败,登录会返登录失败错误码。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            await FTask.CompletedTask;
            return;
        }

        self.Accounts = mongoDatabase.GetCollection<AccountDoc>("accounts");
        Log.Info("AccountServiceComponent 初始化完成,账号账本集合句柄已绑定(accounts)。");
        await FTask.CompletedTask;
    }
}

public sealed class AccountServiceComponentDestroySystem : DestroySystem<AccountServiceComponent>
{
    protected override void Destroy(AccountServiceComponent self)
    {
        self.Accounts = null;
    }
}
