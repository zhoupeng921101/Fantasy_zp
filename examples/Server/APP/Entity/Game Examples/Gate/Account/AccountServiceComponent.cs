using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 账号账本服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持账号集合句柄,登录处理链经 AccountServiceHelper.RegisterOrLogin 走单条原子 upsert
/// (查 + 写一步,防并发同 UUID 双登,SV7)。
/// Account 内存态实体(既有 demo Account / AccountManageComponent)的字段不扩,本子单只补持久层 + 登录时机挂钩
/// (Tier 0 第 1 子单地基,内存态扩字段是 Tier 1+ 议题)。
/// 设计基线:design-docs/35-account-server.md。
/// </summary>
public sealed class AccountServiceComponent : Entity
{
    /// <summary>账号账本集合(accounts),_id = 设备 UUID 字符串。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<AccountDoc>? Accounts;
}
