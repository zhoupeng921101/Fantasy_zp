using Fantasy.Entitas;
using Fantasy.Network;

namespace Fantasy;

public sealed class Account : Entity
{
    public string Name;

    public EntityReference<Session> Session;

    /// <summary>
    /// 玩家身份 id(P0 签发的账号级权威身份,32 位小写 hex)。
    /// 登录处理链在 ClaimOrIssuePlayerId 成功后写入,后续 handler 从会话 → flag.Account.PlayerId 直接拿,
    /// 不必为每次请求回库读 PlayerDoc。
    /// 在局对局档(block_blast_session)按本字段寻址;P1 排行榜的展示名仍走 Account.Name(账号标识占位),两条通道身份语义不同(账号 vs 玩家)。
    /// </summary>
    public string PlayerId = string.Empty;
}