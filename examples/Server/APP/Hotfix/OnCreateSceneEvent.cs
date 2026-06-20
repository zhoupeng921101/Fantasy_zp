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
                scene.AddComponent<PlayerUnitManageComponent>();
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
                // 兑换码服务端权威组件:持有码表/防重记录/全局计数的 MongoDB 集合句柄。
                scene.AddComponent<RedeemServiceComponent>();
                // 排行榜服务端权威组件:持有全服分数集合句柄与榜定义缓存。
                scene.AddComponent<RankServiceComponent>();
                // 邮件服务端权威组件:持有运营模板/定向邮件/领取记录/礼包库的 MongoDB 集合句柄。
                scene.AddComponent<MailServiceComponent>();

                var unit = Entity.Create<Unit>(scene);
                
                await scene.EntityComponent.TransferOut(unit);
                await scene.EntityComponent.TransferIn(unit);
                break;
            }
        }
    }
}