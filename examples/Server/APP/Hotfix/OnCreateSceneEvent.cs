using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using Fantasy.Event;
using Fantasy.IdFactory;

namespace Fantasy;

public sealed class UnitTransferOutSystem : TransferOutSystem<Unit>
{
    protected override async FTask Out(Unit self)
    {
        Log.Debug("UnitTransferOutSystem");
        await FTask.CompletedTask;
    }
}

public sealed class UnitTransferInSystem : TransferInSystem<Unit>
{
    protected override async FTask In(Unit self)
    {
        Log.Debug("UnitTransferInSystem");
        await FTask.CompletedTask;
    }
}

public sealed class OnCreateSceneEvent : AsyncEventSystem<OnCreateScene>
{

    private static long _addressableSceneRunTimeId;

    /// <summary>
    /// Handles the OnCreateScene event.
    /// </summary> 
    /// <param name="self">The OnCreateScene object.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async FTask Handler(OnCreateScene self)
    {
        var scene = self.Scene;

        await FTask.CompletedTask;
        scene.LogDebug("OnCreateSceneEvent");

        switch (scene.SceneType)
        {
            case 6666:
            {
                break;
            }
            case SceneType.Addressable:
            {
                _addressableSceneRunTimeId = scene.RuntimeId;
                break;
            }
            case SceneType.Map:
            {
                // Map 游戏玩法(单位/移动)已移除;Map 场景保留作框架示例(Addressable/Roaming)的目标空场景。
                break;
            }
            case SceneType.Chat:
            {
                break;
            }
            case SceneType.Gate:
            {
                scene.AddComponent<AccountManageComponent>();
                // 账号账本服务端权威组件:持有账号集合(accounts)句柄;
                // 首连自动注册 + 重连刷新末次登录(设计 35),由 LoginGameHandler 经
                // AccountServiceHelper.RegisterOrLogin 在挂会话身份前调用。
                scene.AddComponent<AccountServiceComponent>();
                // 玩家属性账本服务端权威组件:持有玩家属性集合(players)句柄 + 三属性运营配置(初始值 / 上界)。
                // 首登 setOnInsert + 通用变更入口 + 服务端进程内 API + 主动推送(设计 37 第 1 子单),
                // 由 LoginGameHandler 在 RegisterOrLogin 之后调用 PlayerPropertyServiceHelper.InitOrLoad。
                scene.AddComponent<PlayerPropertyServiceComponent>();
                // 兑换码服务端权威组件:持有码表/防重记录/全局计数的 MongoDB 集合句柄。
                scene.AddComponent<RedeemServiceComponent>();
                // 排行榜服务端权威组件:持有全服分数集合句柄与榜定义缓存。
                scene.AddComponent<RankServiceComponent>();
                // 邮件服务端权威组件:持有运营模板/定向邮件/领取记录/礼包库的 MongoDB 集合句柄。
                scene.AddComponent<MailServiceComponent>();
                // 活动系统服务端权威组件:持有活动配置/活动进度的 MongoDB 集合句柄 + 配置内存缓存。
                // 登录触发达标判定(设计 39 §3.5 Login 类),由 LoginGameHandler 在 Online 之后调
                // ActivityEvalHelper.OnLogin(遍历 Type=Login 活动 → counter+1 → 抢占周期键 → 调 32 SendMailTo)。
                // 挂载顺序在 MailServiceComponent 之后:OnLogin 内依赖 MailServiceComponent 投活动结算邮件。
                scene.AddComponent<ActivityServiceComponent>();
                // 云存档(P3)服务端 blob 同步组件:持有 player_cloud_save 集合句柄 + blob 大小上限。
                // 按 playerId 寻址,身份从会话 → Account.PlayerId 取(登录链 ClaimOrIssuePlayerId 后挂载)。
                scene.AddComponent<CloudSaveServiceComponent>();

                var unit = Entity.Create<Unit>(scene);
                
                await scene.EntityComponent.TransferOut(unit);
                await scene.EntityComponent.TransferIn(unit);
                break;
            }
        }
    }
}