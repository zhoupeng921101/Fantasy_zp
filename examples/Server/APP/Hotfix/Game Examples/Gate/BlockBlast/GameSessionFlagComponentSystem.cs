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
        game?.Dispose();
    }
}
