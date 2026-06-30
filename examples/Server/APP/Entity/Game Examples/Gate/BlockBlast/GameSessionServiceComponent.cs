using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// Block Blast 对局持久服务端组件,挂在 Gate Scene 上(玩家会话所在、其 World 配了 MongoDB)。
/// 持有 block_blast_session 集合句柄;落子后存盘、进入对局时按 playerId 读盘恢复。
/// 寻址按 playerId(登录身份锚);服务端是局内态唯一写者,upsert 覆盖式写、无并发版本裁决。
/// MongoDB 不可达时 Sessions 为 null:存盘静默跳过、读盘返 null(新建对局),不阻断对局玩法。
/// </summary>
public sealed class GameSessionServiceComponent : Entity
{
    /// <summary>对局持久集合(block_blast_session),_id = playerId 字符串。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<GameSessionDoc>? Sessions;
}
