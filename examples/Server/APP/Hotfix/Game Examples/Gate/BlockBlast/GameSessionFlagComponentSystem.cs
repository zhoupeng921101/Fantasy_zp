using Fantasy.Entitas.Interface;

namespace Fantasy;

/// <summary>
/// 会话断开 → 清掉其当前权威对局,避免 GameSession 实体在 Gate Scene 泄漏。
/// GameSession 建在 Gate Scene(非会话子级),不随会话级联销毁,故在此显式 Dispose。
/// </summary>
public sealed class GameSessionFlagComponentDestroySystem : DestroySystem<GameSessionFlagComponent>
{
    protected override void Destroy(GameSessionFlagComponent self)
    {
        GameSession? game = self.GameSession;
        game?.Dispose();
    }
}
