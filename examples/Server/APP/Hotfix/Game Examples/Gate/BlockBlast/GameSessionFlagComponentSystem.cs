using Fantasy.Entitas.Interface;

namespace Fantasy;

/// <summary>
/// 会话断开 → 清掉其当前内存 GameSession 实例,避免实体在 Gate Scene 泄漏。
/// GameSession 建在 Gate Scene(非会话子级),不随会话级联销毁,故在此显式 Dispose。
/// 注:仅清内存实例,持久 Doc(block_blast_session)不动 —— 下次进入对局按 playerId 从 Doc 续局恢复。
/// </summary>
public sealed class GameSessionFlagComponentDestroySystem : DestroySystem<GameSessionFlagComponent>
{
    protected override void Destroy(GameSessionFlagComponent self)
    {
        GameSession? game = self.GameSession;
        if (game == null)
        {
            return;
        }

        // 存盘防抖兜底:会话断开(登出 / 关 App)时若有未落盘的步,fire-and-forget flush 一次再销毁内存实例,
        // 把丢失窗口从「防抖间隔」缩到「硬崩溃中途」(优雅断线 0 丢失)。
        // 这里不能用会在 await 后回写 game 的 FlushIfDirty:本处 fire-and-forget 后立即 Dispose,异步体回写会命中已销毁(池化归还)的实体。
        // 故同步判 dirty + 同步 BuildDoc 快照,再 fire Save(异步体只用已构建的 doc,不再触碰 game);实体即将销毁,无需回写已落盘步号。
        if (game.Step > game.LastPersistedStep)
        {
            var service = game.Scene?.GetComponent<GameSessionServiceComponent>();
            if (service != null)
            {
                GameSessionPersistHelper.Save(service, GameSessionHelper.BuildDoc(game)).Coroutine();
            }
        }

        game.Dispose();
    }
}
