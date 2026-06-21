using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家属性账本服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持 players 集合句柄 + 三属性「首登初始值」「类型上界」运营配置(启动期 AwakeSystem 校验:
/// 配置非法 → 服务端启动失败,见 §5.1/§5.3 风险表)。
/// 设计基线:design-docs/37-player-attr-server.md。
/// </summary>
public sealed class PlayerPropertyServiceComponent : Entity
{
    /// <summary>玩家属性账本集合(players),_id = UUID 字符串。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<PlayerDoc>? Players;

    /// <summary>
    /// 玩家三属性变更审计流水集合(player_attr_ledger,设计 44 §3.1)。Init 前 / MongoDB 不可达时为 null。
    /// 每笔 ChangeProperty 写库成功后追加一行(AttrLedgerHelper.AppendAsync),**永不** update / delete(SV14)。
    /// Null 时 ChangeProperty 写库照常成功 + ledger 旁路静默跳过(余额不回滚,沿设计 44 §3.4 「ledger 失败不回滚 players」基线)。
    /// </summary>
    public IMongoCollection<PlayerAttrLedgerDoc>? AttrLedger;

    /// <summary>金币首登初始值(运营配置,默认 0;§3.1 + plan D6 不读 PlayerPrefs 老值)。</summary>
    public long CoinInitial;

    /// <summary>钻石首登初始值(运营配置,默认 0)。</summary>
    public long DiamondInitial;

    /// <summary>体力首登初始值(运营配置,默认 5)。</summary>
    public long StaminaInitial;

    /// <summary>金币类型上界(运营配置,默认 999999999;§3.1)。</summary>
    public long CoinUpperBound;

    /// <summary>钻石类型上界(运营配置,默认 999999;去变现下不大量发放对齐)。</summary>
    public long DiamondUpperBound;

    /// <summary>体力类型上界(运营配置,默认 5;Tier 2+ 加体力上限独立字段后改为读上限字段)。</summary>
    public long StaminaUpperBound;

    /// <summary>本子单的 schema 版本(常量 1;Tier 2+ 加字段时升)。</summary>
    public const int CurrentSchemaVersion = 1;
}
