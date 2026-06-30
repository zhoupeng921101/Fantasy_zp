using Fantasy.Entitas;

namespace Fantasy;

/// <summary>
/// 挂在 Session 上,标记该会话当前所属的 Block Blast 权威对局。
/// 与 GateAccountFlagComponent 同范式:place / snapshot handler 从会话取 flag → 校验 gameId → 拿权威 GameSession。
/// 一会话同时只持一局(开新局覆盖旧局,旧局实例随覆盖 Dispose)。
/// </summary>
public sealed class GameSessionFlagComponent : Entity
{
    public EntityReference<GameSession> GameSession;
}
