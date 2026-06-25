using Fantasy.Async;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家属性账本组件初始化:绑定原生 MongoDB 集合句柄 + 装入运营默认初始值 / 类型上界配置 + 启动期校验。
/// 集合 players 首次 setOnInsert 时由 MongoDB 自动创建(同 mail_template / accounts 先例);_id 主键天然唯一(SV2)。
/// 配置校验失败(初始值 < 0 / 初始值 > 上界 / 上界为负 / 上界为 Int 不安全极值)→ Log.Error + 不绑句柄,
/// 后续 PropertyChangeRequest / 进程内 API 检测 Players == null 返 ServiceUnavailable(沿 35 「MongoDB 不可达」基线)。
/// 设计基线:design-docs/37-player-attr-server.md §3.1 + §5.1。
/// </summary>
public sealed class PlayerPropertyServiceComponentAwakeSystem : AwakeSystem<PlayerPropertyServiceComponent>
{
    /// <summary>金币首登初始值(去变现:玩家从 0 起,运营按需邮件补偿;§读前必看 + §3.2)。</summary>
    private const long DefaultCoinInitial = 0L;

    /// <summary>钻石首登初始值。</summary>
    private const long DefaultDiamondInitial = 0L;

    /// <summary>体力首登初始值(与默认体力上限对齐)。</summary>
    private const long DefaultStaminaInitial = 5L;

    /// <summary>金币类型上界(约 9 位数,远低于 long.MaxValue,防整数溢出,§3.4)。</summary>
    private const long DefaultCoinUpperBound = 999_999_999L;

    /// <summary>钻石类型上界(约 6 位数,与去变现下不大量发放对齐)。</summary>
    private const long DefaultDiamondUpperBound = 999_999L;

    /// <summary>体力类型上界(与体力上限对齐;Tier 2+ 加上限字段后改为读上限)。</summary>
    private const long DefaultStaminaUpperBound = 5L;

    // ---- P2 新增:四种玩法货币默认值 + 限界信任阈值 ----
    // 三货币(soul/piety/exp)的"单次上限"与"总上限"已按客户端口径对齐(同 mail/rank/体力对齐 SV 手抄惯例)。
    // 取值原则:单次上限 = 客户端最大单笔合法发放 + 小余量(是限界信任主杠杆,必须 ≥ 真实最大单笔,否则正常发放被误拒);
    //          总上限 = 宽松 sanity 天花板(非游戏硬上限,只挡荒谬值,宁松勿紧——卡正常重度玩家收益小,主杠杆是单次上限)。
    // 数值源自客户端代码常量与公式(ChestSystem/TempleConfig/MergeOrderConfig 等硬编码),非读 Luban 数据表;
    // 若线上有活动/充值/季票等额外大额发放路径,单次上限可能需再放宽。源改了需同步更新此处。

    /// <summary>
    /// 灵力默认初始 0 / 总上限 100_000(宽松 sanity 天花板)。
    /// 客户端产销量级:最大单笔发放=史诗宝箱 200(`ChestSystem` 宝箱奖励档,史诗 200/稀有 80/普通 20);
    /// 最大消耗=祈愿 20。总量典型几千,10 万为宽松天花板。
    /// </summary>
    private const long DefaultSoulPowerInitial = 0L;
    private const long DefaultSoulPowerUpperBound = 100_000L;

    /// <summary>
    /// 虔诚币默认初始 0 / 总上限 1_000_000(宽松 sanity 天花板)。
    /// 客户端产销量级:最大单笔发放=特殊订单 难度512 × `TempleConfig.PietyPerDifficulty`(30) × `SpecialPietyMult`(2) = 30_720;
    /// 最大消耗=修庙单厅 3_250(`TempleConfig` 厅造价数组顶值)。全解锁需~22_500,重度玩家更多,100 万为宽松天花板。
    /// </summary>
    private const long DefaultPietyInitial = 0L;
    private const long DefaultPietyUpperBound = 1_000_000L;

    /// <summary>
    /// 守护者经验默认初始 0 / 总上限 2_000_000(宽松 sanity 天花板)。
    /// 客户端产销量级:最大单笔发放=修庙单厅 3_250(= 该厅造价,`TempleConfig` 厅造价数组顶值);无消耗(单向)。
    /// L60 累计~539_200,200 万为宽松天花板。
    /// </summary>
    private const long DefaultGuardianExpInitial = 0L;
    private const long DefaultGuardianExpUpperBound = 2_000_000L;

    /// <summary>
    /// 玩法体力默认初始 / 上界。值手抄自客户端 xlsx 口径(同 mail/rank/activity 服务端镜像先例):
    /// Initial=20 ← 客户端 `MergeOrderConfig.EnergyStart` 常量(MergeOrderConfig.cs);
    /// UpperBound=30 ← 客户端 `GlobalConfigMgr.EnergyRecoverCapValue` 默认值(global.xlsx id=4)。
    /// 注:本仓库无 global.xlsx,值取自客户端代码内文档化默认;源表被策划调过则需以真表为准再校。
    /// 源表改了需同步更新此处。
    /// </summary>
    private const long DefaultEnergyInitial = 20L;
    private const long DefaultEnergyUpperBound = 30L;

    /// <summary>
    /// 体力恢复 tick 间隔 360000ms = 360 秒。值手抄自客户端 `GlobalConfigMgr.EnergyRecoverIntervalDefault`
    /// (global.xlsx id=3 间隔段默认 360 秒)。源表改了需同步。
    /// </summary>
    private const long DefaultEnergyRecoverIntervalMs = 360_000L;
    /// <summary>
    /// 体力每 tick 恢复 1 点。值手抄自客户端 `GlobalConfigMgr.EnergyRecoverAmountDefault`
    /// (global.xlsx id=3 点数段默认 1)。源表改了需同步。
    /// </summary>
    private const long DefaultEnergyRecoverPerTick = 1L;

    /// <summary>
    /// Coin/Diamond 单次 delta 上限(占位):远低于类型上界,挡粗暴改值。
    /// 真实业务侧最大单笔幅度(单次奖励 / 单次消耗)确定后,按倍率调参。
    /// </summary>
    private const long DefaultCoinSingleDeltaLimit = 1_000_000L;
    private const long DefaultDiamondSingleDeltaLimit = 100_000L;
    private const long DefaultStaminaSingleDeltaLimit = 5L;
    /// <summary>
    /// 灵力单次 delta 上限 500。依据:客户端最大单笔合法发放 = 史诗宝箱 200(`ChestSystem` 宝箱奖励档),
    /// 留余量含 2 箱。是限界信任主杠杆,必须 ≥ 真实最大单笔。源(宝箱档/祈愿消耗)改了需同步重审。
    /// </summary>
    private const long DefaultSoulPowerSingleDeltaLimit = 500L;
    /// <summary>
    /// 虔诚币单次 delta 上限 35_000。依据:客户端最大单笔合法发放 = 特殊订单 难度512
    /// × `TempleConfig.PietyPerDifficulty`(30) × `SpecialPietyMult`(2) = 30_720,余量到 35_000。
    /// 是限界信任主杠杆,必须 ≥ 真实最大单笔。源(订单难度上限/系数/修庙造价)改了需同步重审。
    /// </summary>
    private const long DefaultPietySingleDeltaLimit = 35_000L;
    /// <summary>
    /// 守护者经验单次 delta 上限 5_000。依据:客户端最大单笔合法发放 = 修庙单厅 3_250
    /// (`TempleConfig` 厅造价数组顶值,经验 = 该厅造价),余量到 5_000。
    /// 是限界信任主杠杆,必须 ≥ 真实最大单笔。源(厅造价表)改了需同步重审。
    /// </summary>
    private const long DefaultGuardianExpSingleDeltaLimit = 5_000L;
    /// <summary>
    /// 体力单次 delta 上限 30。依据:客户端最大单笔合法发放 = 修庙满补
    /// `TempleConfig.TempleRepairEnergy`(=30,等于上限本身回满)。
    /// 与 EnergyUpperBound 同值是 ValidateConfig 允许的上限(校验要求 ∈ (0, UpperBound])。
    /// 旧占位 120(=4 倍真实上限)形同虚设,且超 UpperBound=30 会启动期拒服。源表改了需同步重审。
    /// </summary>
    private const long DefaultEnergySingleDeltaLimit = 30L;

    /// <summary>变更频率最小间隔(占位 100ms):同账号同属性 100ms 内重复变更视为脚本刷,拒。</summary>
    private const long DefaultPropertyChangeMinIntervalMs = 100L;

    protected override void Awake(PlayerPropertyServiceComponent self)
    {
        // 装入运营默认配置(本子单未引入运营热改面,常量即权威源;Tier 2+ 真要热改时,
        // 此处改读 MongoDB 配置集合 / Luban 配置同源,沿 mail / rank 先例)。
        self.CoinInitial = DefaultCoinInitial;
        self.DiamondInitial = DefaultDiamondInitial;
        self.StaminaInitial = DefaultStaminaInitial;
        self.CoinUpperBound = DefaultCoinUpperBound;
        self.DiamondUpperBound = DefaultDiamondUpperBound;
        self.StaminaUpperBound = DefaultStaminaUpperBound;

        // P2 新增四货币 + 限界信任阈值。
        self.SoulPowerInitial = DefaultSoulPowerInitial;
        self.SoulPowerUpperBound = DefaultSoulPowerUpperBound;
        self.PietyInitial = DefaultPietyInitial;
        self.PietyUpperBound = DefaultPietyUpperBound;
        self.GuardianExpInitial = DefaultGuardianExpInitial;
        self.GuardianExpUpperBound = DefaultGuardianExpUpperBound;
        self.EnergyInitial = DefaultEnergyInitial;
        self.EnergyUpperBound = DefaultEnergyUpperBound;
        self.EnergyRecoverIntervalMs = DefaultEnergyRecoverIntervalMs;
        self.EnergyRecoverPerTick = DefaultEnergyRecoverPerTick;
        self.CoinSingleDeltaLimit = DefaultCoinSingleDeltaLimit;
        self.DiamondSingleDeltaLimit = DefaultDiamondSingleDeltaLimit;
        self.StaminaSingleDeltaLimit = DefaultStaminaSingleDeltaLimit;
        self.SoulPowerSingleDeltaLimit = DefaultSoulPowerSingleDeltaLimit;
        self.PietySingleDeltaLimit = DefaultPietySingleDeltaLimit;
        self.GuardianExpSingleDeltaLimit = DefaultGuardianExpSingleDeltaLimit;
        self.EnergySingleDeltaLimit = DefaultEnergySingleDeltaLimit;
        self.PropertyChangeMinIntervalMs = DefaultPropertyChangeMinIntervalMs;

        // 启动期校验:配置非法 → 服务端拒服(§5.1 + §5.3 风险表)。
        // 这里用 Log.Error + 不绑句柄(等价于「服务不可用」),不抛异常断 Awake(框架要求 AwakeSystem 不抛)。
        if (!ValidateConfig(self))
        {
            Log.Error("PlayerPropertyServiceComponent: 配置非法(初始值 / 类型上界),拒服;玩家登录会返登录失败 / 变更请求会返 ServiceUnavailable。");
            return;
        }

        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    /// <summary>启动期校验:初始值 ∈ [0, 上界],上界 ∈ [0, long.MaxValue / 2](留 delta 加法不溢出空间)。</summary>
    private static bool ValidateConfig(PlayerPropertyServiceComponent self)
    {
        const long maxSafeUpperBound = long.MaxValue / 2;

        // 七属性上界、初始、单次 delta 上限的统一校验(P2 扩四类一并检查)。
        // name 字段仅供 Log 展示用,故直接用字符串字面量(组件本身字段名是 CoinInitial / CoinUpperBound,
        // 这里要表达的是 PropertyType 的语义名)。
        var checks = new (long initial, long upperBound, long singleDeltaLimit, string name)[]
        {
            (self.CoinInitial, self.CoinUpperBound, self.CoinSingleDeltaLimit, "Coin"),
            (self.DiamondInitial, self.DiamondUpperBound, self.DiamondSingleDeltaLimit, "Diamond"),
            (self.StaminaInitial, self.StaminaUpperBound, self.StaminaSingleDeltaLimit, "Stamina"),
            (self.SoulPowerInitial, self.SoulPowerUpperBound, self.SoulPowerSingleDeltaLimit, "SoulPower"),
            (self.PietyInitial, self.PietyUpperBound, self.PietySingleDeltaLimit, "Piety"),
            (self.GuardianExpInitial, self.GuardianExpUpperBound, self.GuardianExpSingleDeltaLimit, "GuardianExp"),
            (self.EnergyInitial, self.EnergyUpperBound, self.EnergySingleDeltaLimit, "Energy"),
        };
        foreach (var c in checks)
        {
            if (c.upperBound < 0 || c.upperBound > maxSafeUpperBound)
            {
                Log.Error($"PlayerPropertyServiceComponent: {c.name}UpperBound={c.upperBound} 非法(必须 ∈ [0, long.MaxValue/2])。");
                return false;
            }
            if (c.initial < 0 || c.initial > c.upperBound)
            {
                Log.Error($"PlayerPropertyServiceComponent: {c.name}Initial={c.initial} 非法(必须 ∈ [0, {c.name}UpperBound={c.upperBound}])。");
                return false;
            }
            // 单次 delta 上限必须 ∈ (0, upperBound]:0 = 完全拒变更、超 upperBound = 形同虚设。
            if (c.singleDeltaLimit <= 0 || c.singleDeltaLimit > c.upperBound)
            {
                Log.Error($"PlayerPropertyServiceComponent: {c.name}SingleDeltaLimit={c.singleDeltaLimit} 非法(必须 ∈ (0, {c.name}UpperBound={c.upperBound}])。");
                return false;
            }
        }

        // 体力恢复参数:间隔 > 0 且每 tick 恢复 > 0;频率间隔 >= 0(0 = 关掉频率限制)。
        if (self.EnergyRecoverIntervalMs <= 0L || self.EnergyRecoverPerTick <= 0L)
        {
            Log.Error($"PlayerPropertyServiceComponent: EnergyRecoverIntervalMs={self.EnergyRecoverIntervalMs}/PerTick={self.EnergyRecoverPerTick} 非法(必须 > 0)。");
            return false;
        }
        if (self.PropertyChangeMinIntervalMs < 0L)
        {
            Log.Error($"PlayerPropertyServiceComponent: PropertyChangeMinIntervalMs={self.PropertyChangeMinIntervalMs} 非法(必须 >= 0)。");
            return false;
        }

        return true;
    }

    private static async FTask Init(PlayerPropertyServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):players 集合无法绑定,后续登录与变更会返 ServiceUnavailable。
            // 属预期环境条件,用 Warning 不用 Error(同 MailServiceComponentSystem 先例)。
            Log.Warning("PlayerPropertyServiceComponent: MongoDB 实例不可用,玩家属性 setOnInsert / 变更将无法持久,登录会返登录失败,变更请求会返 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            await FTask.CompletedTask;
            return;
        }

        self.Players = mongoDatabase.GetCollection<PlayerDoc>("players");

        // players 集合 PlayerId 部分唯一索引(身份锚不可碰撞,DB 端兜底 TOCTOU)。
        // PartialFilterExpression: PlayerId > "" → 字段缺失或空串都不受唯一约束(都不大于 ""),
        // 只对真实签发的非空 playerId 加唯一约束。
        // 不用 $ne/$not — MongoDB partial index 不支持(运行时报 "Expression not supported in partial index: $not")。
        // 不用 $exists:true — 既不排除空串,旧档 PlayerId == "" 多条会撞唯一约束。
        // $gt 是 partial index 受支持的比较算子,语义恰好「非空字符串」(BSON 字符串比较,"" 最小)。
        // 一旦签发/认领写入非空值,任何并发账号尝试写入同一值会被 DB 拒(E11000 duplicate key),
        // 应用层查重 + 命中重复键退回服务端生成(见 PlayerPropertyServiceHelper.ClaimOrIssuePlayerId)。
        // 索引建失败(已存在等)Warning 不阻断,但失败后失去 DB 层兜底,应用层查重仍是 fast path。
        try
        {
            var playerIdUnique = new CreateIndexModel<PlayerDoc>(
                Builders<PlayerDoc>.IndexKeys.Ascending(x => x.PlayerId),
                new CreateIndexOptions<PlayerDoc>
                {
                    Name = "ux_player_id",
                    Unique = true,
                    PartialFilterExpression = Builders<PlayerDoc>.Filter.Gt(x => x.PlayerId, string.Empty)
                });
            await self.Players.Indexes.CreateOneAsync(playerIdUnique);
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceComponent: players.PlayerId 唯一索引创建警告(可能已存在或配置冲突),err={e.Message}");
        }

        // player_attr_ledger 集合句柄 + 建索引(设计 44 §3.1 + §3.2,SV2)。
        // 集合在首次 InsertOne 时由 MongoDB 自动创建(同 mail_template / accounts / players 先例);
        // CreateMany 索引建好后,后续 Append 直接命中查询索引。
        // 索引建失败(MongoDB 抖动等)→ Warning 不阻断 Players 句柄绑定;
        // ledger 句柄仍绑(只是查询走全表扫,审计完整性不破)。
        var ledger = mongoDatabase.GetCollection<PlayerAttrLedgerDoc>("player_attr_ledger");
        try
        {
            // 复合索引 (Account ASC, Timestamp DESC):核心查询「某账号最近 N 笔」直接命中(SV13)。
            var byAccount = new CreateIndexModel<PlayerAttrLedgerDoc>(
                Builders<PlayerAttrLedgerDoc>.IndexKeys
                    .Ascending(x => x.Account)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_account_ts_desc" });
            // 单字段索引 (Timestamp DESC):运营全局扫 + Tier 2+ 挂 TTL 用(O7)。
            var byTs = new CreateIndexModel<PlayerAttrLedgerDoc>(
                Builders<PlayerAttrLedgerDoc>.IndexKeys.Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_ts_desc" });
            await ledger.Indexes.CreateManyAsync(new[] { byAccount, byTs });
        }
        catch (MongoException e)
        {
            // 索引创建失败(可能重复 / 配置变更冲突):不阻断 ledger 句柄绑定。
            Log.Warning($"PlayerPropertyServiceComponent: player_attr_ledger 索引创建警告(可能重复存在),err={e.Message}");
        }
        self.AttrLedger = ledger;

        Log.Info($"PlayerPropertyServiceComponent 初始化完成,玩家属性账本集合句柄已绑定(players + player_attr_ledger);" +
                 $"初始值[coin={self.CoinInitial} diamond={self.DiamondInitial} stamina={self.StaminaInitial}]," +
                 $"上界[coin={self.CoinUpperBound} diamond={self.DiamondUpperBound} stamina={self.StaminaUpperBound}].");
    }
}

public sealed class PlayerPropertyServiceComponentDestroySystem : DestroySystem<PlayerPropertyServiceComponent>
{
    protected override void Destroy(PlayerPropertyServiceComponent self)
    {
        self.Players = null;
        self.AttrLedger = null;
    }
}
