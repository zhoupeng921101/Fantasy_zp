using System.Collections.Concurrent;
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

    // ---- P2 新增:四种玩法货币的初始值 / 类型上界 / 单次变更上限 / 频率(2026-06 全栈迁移)----
    // 阈值全部是**占位值**,待真实玩法产销速率确定后按 design-docs/playflow 调参。
    // 集中放在本组件,沿三老属性「配置即权威源」基线,Tier 2+ 真要热改时改读 MongoDB 配置集合。

    /// <summary>灵力首登初始值。</summary>
    public long SoulPowerInitial;
    /// <summary>灵力类型上界(余额上限)。</summary>
    public long SoulPowerUpperBound;

    /// <summary>虔诚币首登初始值。</summary>
    public long PietyInitial;
    /// <summary>虔诚币类型上界。</summary>
    public long PietyUpperBound;

    /// <summary>守护者经验首登初始值。</summary>
    public long GuardianExpInitial;
    /// <summary>守护者经验类型上界。</summary>
    public long GuardianExpUpperBound;

    /// <summary>体力首登初始值(玩法体力,与原 Stamina 不复用)。</summary>
    public long EnergyInitial;
    /// <summary>体力类型上界(= 体力 cap)。</summary>
    public long EnergyUpperBound;

    /// <summary>
    /// 体力恢复 tick 间隔(Unix 毫秒)。每过这么多毫秒恢复 EnergyRecoverPerTick 点。
    /// **占位值**,真实速率按玩法产销节律确定。
    /// </summary>
    public long EnergyRecoverIntervalMs;
    /// <summary>体力每 tick 恢复点数(占位 1)。</summary>
    public long EnergyRecoverPerTick;

    /// <summary>
    /// 单次 ChangeProperty 的 delta 绝对值上限(限界信任·防粗暴改值,挡 |delta| 超过此值的客户端伪造)。
    /// 比类型上界严格得多:类型上界是「总余额上限」,本上限是「单笔变更上限」。
    /// **占位值**,真实业务侧最大单笔(如一次奖励/一次消耗)幅度确定后按比例调参。
    /// </summary>
    public long CoinSingleDeltaLimit;
    public long DiamondSingleDeltaLimit;
    public long StaminaSingleDeltaLimit;
    public long SoulPowerSingleDeltaLimit;
    public long PietySingleDeltaLimit;
    public long GuardianExpSingleDeltaLimit;
    public long EnergySingleDeltaLimit;

    /// <summary>
    /// 同账号同属性两次变更最小间隔(Unix 毫秒,限界信任·频率限制)。短于此判定脚本刷分,拒。
    /// 进程内 ConcurrentDictionary 持(account|propertyType → 上次变更时刻),进程重启清零(沿 P1 RankAntiCheatPolicy 同款做法,反作弊收益高于代价低于跨进程复杂度)。
    /// **占位值** 100ms;真实玩法节律确定后调参。
    /// </summary>
    public long PropertyChangeMinIntervalMs;

    /// <summary>
    /// 进程内频率追踪表(限界信任·防脚本刷:key = "account|propertyType",value = 上次变更时刻 Unix 毫秒)。
    /// 进程重启清零(沿 P1 RankAntiCheatPolicy 同款做法;攻击者重启服务端的成本远高于刷分收益)。
    /// </summary>
    public readonly ConcurrentDictionary<string, long> LastChangeAtMs = new ConcurrentDictionary<string, long>();

    /// <summary>玩家数据 schema 版本(常量 3;P2 加四种玩法货币 + 体力恢复时刻后升至 3)。</summary>
    public const int CurrentSchemaVersion = 3;
}
