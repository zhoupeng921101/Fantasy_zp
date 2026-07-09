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
    /// 玩法体力默认初始 / 上界。
    /// Initial=20 ← 客户端 `MergeOrderConfig.EnergyStart` 常量(B 类,Stage 2 入表);
    /// UpperBound=9999 = 存储硬顶 / ChangeProperty 单笔变更后余额上界,远大于软上限。
    ///   订单交付 +8、内购 / 许愿 / 盲盒等主动来源允许把体力顶到软上限以上(规则:其他来源不被软上限钳制);
    ///   9999 仅作 sanity 天花板挡荒谬值,真业务远不可能撞顶。
    /// 软上限 / 恢复 tick 改读 Luban global.xlsx(id=4/id=3),见 Awake 内。
    /// </summary>
    private const long DefaultEnergyInitial = 20L;
    private const long DefaultEnergyUpperBound = 9999L;

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
    /// 体力单次 delta 上限 30(仅作客户端 RPC 路径限界信任,服务端权威发放路径 serverAuthoritative=true 绕过)。
    /// 依据:客户端最大单笔合法发放 = 修庙满补 `TempleConfig.TempleRepairEnergy`(=30)。
    /// 与 EnergyRecoverSoftCap 同值是巧合 — 它防的是客户端伪造大额 delta,不是体力余额上限。
    /// 服务端订单交付 +8 走 serverAuthoritative 路径,不受此限亦不受 SoftCap 限,只受 EnergyUpperBound=9999 限。
    /// 源表改了需同步重审。
    /// </summary>
    private const long DefaultEnergySingleDeltaLimit = 30L;

    /// <summary>变更频率最小间隔(占位 100ms):同账号同属性 100ms 内重复变更视为脚本刷,拒。</summary>
    private const long DefaultPropertyChangeMinIntervalMs = 100L;

    // ---- P3 新增:五种元层进度计数器默认值 + 限界信任阈值(原云存档 blob 迁出第 1 批,2026-07)----
    // 全部 Initial=0(全新玩家进度为 0)。上界 = 宽松 sanity 天花板(纯挡荒谬值,非玩法硬上限);
    // 单次 delta 上限 = 限界信任主杠杆,取值远大于「玩法一次结算的最大合法跳变」以避免误拒(拿不准从宽)。
    // 这些是**占位值**:女神/章节/修缮的真实产出速率与硬上限来自客户端玩法配置(章节数、修缮项总数、女神满级等),
    // 待第 4/5 批抽奖/合成迁移把玩法产出建模上服务端时再收紧;本批只做「计数器权威落账」,不建模产出逻辑。

    /// <summary>
    /// 女神评级(= 清屏累计好评计数):初始 0。上界 + 单次上限运行时读 Luban global.xlsx id=7 GoddessMaxCount(满档清屏次数),
    /// 见 Awake。上界 = 满档值使全清 +1 到满即被原子写 OverLimit 拒(满档停住等领取,无需额外状态位);
    /// 单次上限 = 同值(一次全清合法 delta=1,恒 ≤ 满档值,校验 singleDeltaLimit ∈ (0, upperBound] 成立)。
    /// </summary>
    private const long DefaultGoddessRatingInitial = 0L;
    /// <summary>女神满档清屏次数缺表回退默认(= global.xlsx id=7 缺失时;与客户端默认一致)。</summary>
    private const int DefaultGoddessMaxCount = 10;

    /// <summary>章节解锁数:初始 0 / 上界 10_000 / 单次上限 100。</summary>
    private const long DefaultUnlockedChapterInitial = 0L;
    private const long DefaultUnlockedChapterUpperBound = 10_000L;
    private const long DefaultUnlockedChapterSingleDeltaLimit = 100L;

    /// <summary>盲盒计数:初始 0 / 上界 1_000_000 / 单次上限 1_000(攒/开盒批量跳变留余量;可增可减)。</summary>
    private const long DefaultBlindBoxCountInitial = 0L;
    private const long DefaultBlindBoxCountUpperBound = 1_000_000L;
    private const long DefaultBlindBoxCountSingleDeltaLimit = 1_000L;

    /// <summary>神庙修缮计数:初始 0 / 上界 100_000 / 单次上限 1_000。</summary>
    private const long DefaultTempleRepairedInitial = 0L;
    private const long DefaultTempleRepairedUpperBound = 100_000L;
    private const long DefaultTempleRepairedSingleDeltaLimit = 1_000L;

    /// <summary>神庙修缮游标:初始 0 / 上界 100_000 / 单次上限 1_000。</summary>
    private const long DefaultNextRepairIndexInitial = 0L;
    private const long DefaultNextRepairIndexUpperBound = 100_000L;
    private const long DefaultNextRepairIndexSingleDeltaLimit = 1_000L;

    // ---- 头像 / 头像框服务端权威默认配置(原云存档 blob 迁出第 2 批·子批 2c,2026-07)----
    // 当前佩戴 id 缺省与客户端默认对齐(头像 1 / 框 101,= 客户端 PlayerInfo.DefaultAvatarId/DefaultFrameId)。
    // id 合法段 sanity 边界:客户端编排头像用 1–100 段、框用 101+ 段(见 AvatarEntry.Id 注)。上界取宽松天花板,
    //   仅挡荒谬值;真正防冒解锁的是「换装校验目标在解锁集合内」+「解锁上报走 client-report 幂等 addToSet」。
    // 段边界 / 集合上限均为**占位值**,后续引入服务端头像配置表(或与客户端 Luban avatar 表同源)时收紧。

    /// <summary>当前佩戴头像 id 缺省(= 客户端 PlayerInfo.DefaultAvatarId=1)。</summary>
    private const int DefaultCurrentAvatarIdInitial = 1;
    /// <summary>当前佩戴头像框 id 缺省(= 客户端 PlayerInfo.DefaultFrameId=101)。</summary>
    private const int DefaultCurrentFrameIdInitial = 101;

    /// <summary>头像合法 id 段 [1, 100](客户端编排头像用 1–100 段)。</summary>
    private const int DefaultMinAvatarId = 1;
    private const int DefaultMaxAvatarId = 100;
    /// <summary>头像框合法 id 段 [101, 100000](客户端编排框用 101+ 段;上界宽松 sanity 天花板)。</summary>
    private const int DefaultMinFrameId = 101;
    private const int DefaultMaxFrameId = 100_000;

    /// <summary>单个已解锁集合大小上限 4096(宽松 sanity 天花板,防客户端灌爆文档;正常玩家远不可能撞顶)。</summary>
    private const int DefaultUnlockedSetMaxSize = 4096;

    /// <summary>修饰操作(换装 / 解锁上报)频率最小间隔(占位 100ms):同账号 100ms 内重复视为脚本刷,拒。</summary>
    private const long DefaultCosmeticMinIntervalMs = 100L;

    // ---- 皮肤态 / 神庙装饰标志服务端权威 sanity 配置默认(原云存档 blob 迁出第 3 批·子批 3b,2026-07)----
    // client-report 限界信任:皮肤 / 装饰纯装饰低危,只做基本 sanity(SkinMono ∈ {0,1} 由 helper 直判;
    //   SkinMonoId / TempleDecorated 落合法段)。段边界均为**占位值**(宽松天花板,只挡荒谬值),
    //   后续引入服务端皮肤 sprite 段 / 神庙厅数配置(或与客户端 Luban 同源)时收紧。

    /// <summary>单色皮肤 id 合法段 [1, 100000](宽松 sanity;彩色态哨兵 -1 由 helper 单独放行,不在此段内)。</summary>
    private const int DefaultSkinMonoIdMin = 1;
    private const int DefaultSkinMonoIdMax = 100_000;

    /// <summary>已装饰厅数标量上界 100000(宽松 sanity 天花板,与 TempleRepaired 上界同量级;真实厅数远不可能撞顶)。</summary>
    private const long DefaultTempleDecoratedMax = 100_000L;

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
        // 体力恢复参数:读 Luban global.xlsx,与客户端 GlobalConfigMgr 同源。
        //   SoftCap   ← id=4 EnergyRecoverCap(默认 30,= 客户端 MergeOrderConfig.EnergyCap 旧值)
        //   Interval  ← id=3 EnergyRecoverSeconds 的 interval 段(复合 "amount#interval";缺表/缺段默认 360 秒)
        //   PerTick   ← 同 id=3 的 amount 段(默认 1 点)
        // Tables 加载失败时 GlobalCfg.* 全部走默认值,等价旧硬编码,行为不回归。
        self.EnergyRecoverSoftCap = GlobalCfg.GetInt(GlobalCfg.EnergyRecoverCap, 30);
        var (energyRecoverAmount, energyRecoverInterval) = GlobalCfg.ParseEnergyRecover();
        self.EnergyRecoverIntervalMs = energyRecoverInterval * 1000L;
        self.EnergyRecoverPerTick = energyRecoverAmount;
        self.CoinSingleDeltaLimit = DefaultCoinSingleDeltaLimit;
        self.DiamondSingleDeltaLimit = DefaultDiamondSingleDeltaLimit;
        self.StaminaSingleDeltaLimit = DefaultStaminaSingleDeltaLimit;
        self.SoulPowerSingleDeltaLimit = DefaultSoulPowerSingleDeltaLimit;
        self.PietySingleDeltaLimit = DefaultPietySingleDeltaLimit;
        self.GuardianExpSingleDeltaLimit = DefaultGuardianExpSingleDeltaLimit;
        self.EnergySingleDeltaLimit = DefaultEnergySingleDeltaLimit;
        self.PropertyChangeMinIntervalMs = DefaultPropertyChangeMinIntervalMs;

        // P3 五元层进度计数器。
        self.GoddessRatingInitial = DefaultGoddessRatingInitial;
        // 女神评级上界 + 单次上限读 global.xlsx id=7 GoddessMaxCount(满档清屏次数,缺表回退 10):
        //   上界 = 满档值 → 全清 +1 到满即被原子写 OverLimit 拒(满档停住,客户端发 claim RPC 领取后服务端清零 → 再循环);
        //   单次上限 = 同值 → 一次全清合法 delta=1 恒 ≤ 满档值不误拒,且满足校验 singleDeltaLimit ∈ (0, 上界]。
        // 改 global id=7 即改满档门槛,不改代码。
        long goddessMaxCount = GlobalCfg.GetInt(GlobalCfg.GoddessMaxCount, DefaultGoddessMaxCount);
        self.GoddessRatingUpperBound = goddessMaxCount;
        self.GoddessRatingSingleDeltaLimit = goddessMaxCount;
        self.UnlockedChapterInitial = DefaultUnlockedChapterInitial;
        self.UnlockedChapterUpperBound = DefaultUnlockedChapterUpperBound;
        self.UnlockedChapterSingleDeltaLimit = DefaultUnlockedChapterSingleDeltaLimit;
        self.BlindBoxCountInitial = DefaultBlindBoxCountInitial;
        self.BlindBoxCountUpperBound = DefaultBlindBoxCountUpperBound;
        self.BlindBoxCountSingleDeltaLimit = DefaultBlindBoxCountSingleDeltaLimit;
        self.TempleRepairedInitial = DefaultTempleRepairedInitial;
        self.TempleRepairedUpperBound = DefaultTempleRepairedUpperBound;
        self.TempleRepairedSingleDeltaLimit = DefaultTempleRepairedSingleDeltaLimit;
        self.NextRepairIndexInitial = DefaultNextRepairIndexInitial;
        self.NextRepairIndexUpperBound = DefaultNextRepairIndexUpperBound;
        self.NextRepairIndexSingleDeltaLimit = DefaultNextRepairIndexSingleDeltaLimit;

        // 头像 / 头像框服务端权威默认配置(2c)。
        self.CurrentAvatarIdInitial = DefaultCurrentAvatarIdInitial;
        self.CurrentFrameIdInitial = DefaultCurrentFrameIdInitial;
        self.MinAvatarId = DefaultMinAvatarId;
        self.MaxAvatarId = DefaultMaxAvatarId;
        self.MinFrameId = DefaultMinFrameId;
        self.MaxFrameId = DefaultMaxFrameId;
        self.UnlockedSetMaxSize = DefaultUnlockedSetMaxSize;
        self.CosmeticMinIntervalMs = DefaultCosmeticMinIntervalMs;

        // 皮肤态 / 神庙装饰标志服务端权威 sanity 配置(3b)。
        self.SkinMonoIdMin = DefaultSkinMonoIdMin;
        self.SkinMonoIdMax = DefaultSkinMonoIdMax;
        self.TempleDecoratedMax = DefaultTempleDecoratedMax;

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
            // P3 五元层进度计数器(同套校验:初始 ∈ [0, 上界]、上界 ∈ [0, long.MaxValue/2]、单次上限 ∈ (0, 上界])。
            (self.GoddessRatingInitial, self.GoddessRatingUpperBound, self.GoddessRatingSingleDeltaLimit, "GoddessRating"),
            (self.UnlockedChapterInitial, self.UnlockedChapterUpperBound, self.UnlockedChapterSingleDeltaLimit, "UnlockedChapter"),
            (self.BlindBoxCountInitial, self.BlindBoxCountUpperBound, self.BlindBoxCountSingleDeltaLimit, "BlindBoxCount"),
            (self.TempleRepairedInitial, self.TempleRepairedUpperBound, self.TempleRepairedSingleDeltaLimit, "TempleRepaired"),
            (self.NextRepairIndexInitial, self.NextRepairIndexUpperBound, self.NextRepairIndexSingleDeltaLimit, "NextRepairIndex"),
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
        // 被动恢复软上限:必须 > 0 且 <= EnergyUpperBound(软上限不可超硬顶,否则恢复钳上限与存储上界语义冲突)。
        if (self.EnergyRecoverSoftCap <= 0L || self.EnergyRecoverSoftCap > self.EnergyUpperBound)
        {
            Log.Error($"PlayerPropertyServiceComponent: EnergyRecoverSoftCap={self.EnergyRecoverSoftCap} 非法(必须 ∈ (0, EnergyUpperBound={self.EnergyUpperBound}])。");
            return false;
        }
        if (self.PropertyChangeMinIntervalMs < 0L)
        {
            Log.Error($"PlayerPropertyServiceComponent: PropertyChangeMinIntervalMs={self.PropertyChangeMinIntervalMs} 非法(必须 >= 0)。");
            return false;
        }

        // 头像 / 头像框服务端权威配置校验(2c):id 段边界升序且正、集合上限 > 0、频率间隔 >= 0、
        // 当前 id 缺省落在对应段内(否则首登默认佩戴一个非法 id,换装校验 / sanity 会永远拒)。
        if (self.MinAvatarId <= 0 || self.MaxAvatarId < self.MinAvatarId ||
            self.MinFrameId <= 0 || self.MaxFrameId < self.MinFrameId)
        {
            Log.Error($"PlayerPropertyServiceComponent: 头像 / 框 id 段非法(avatar=[{self.MinAvatarId},{self.MaxAvatarId}] frame=[{self.MinFrameId},{self.MaxFrameId}],须正且下 <= 上)。");
            return false;
        }
        if (self.CurrentAvatarIdInitial < self.MinAvatarId || self.CurrentAvatarIdInitial > self.MaxAvatarId ||
            self.CurrentFrameIdInitial < self.MinFrameId || self.CurrentFrameIdInitial > self.MaxFrameId)
        {
            Log.Error($"PlayerPropertyServiceComponent: 当前佩戴 id 缺省越段(avatar={self.CurrentAvatarIdInitial}∉[{self.MinAvatarId},{self.MaxAvatarId}] 或 frame={self.CurrentFrameIdInitial}∉[{self.MinFrameId},{self.MaxFrameId}])。");
            return false;
        }
        if (self.UnlockedSetMaxSize <= 0)
        {
            Log.Error($"PlayerPropertyServiceComponent: UnlockedSetMaxSize={self.UnlockedSetMaxSize} 非法(必须 > 0)。");
            return false;
        }
        if (self.CosmeticMinIntervalMs < 0L)
        {
            Log.Error($"PlayerPropertyServiceComponent: CosmeticMinIntervalMs={self.CosmeticMinIntervalMs} 非法(必须 >= 0)。");
            return false;
        }

        // 皮肤态 / 神庙装饰标志服务端权威 sanity 配置校验(3b):id 段升序且正、装饰上界非负。
        if (self.SkinMonoIdMin <= 0 || self.SkinMonoIdMax < self.SkinMonoIdMin)
        {
            Log.Error($"PlayerPropertyServiceComponent: 单色皮肤 id 段非法(=[{self.SkinMonoIdMin},{self.SkinMonoIdMax}],须正且下 <= 上)。");
            return false;
        }
        if (self.TempleDecoratedMax < 0L)
        {
            Log.Error($"PlayerPropertyServiceComponent: TempleDecoratedMax={self.TempleDecoratedMax} 非法(必须 >= 0)。");
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

        // player_item_ledger 集合句柄 + 建索引(道具持有变更流水,沿 player_attr_ledger 同规约:
        // insert-only、失败 Warning 不阻断、查询索引同形)。
        var itemLedger = mongoDatabase.GetCollection<PlayerItemLedgerDoc>("player_item_ledger");
        try
        {
            var itemByAccount = new CreateIndexModel<PlayerItemLedgerDoc>(
                Builders<PlayerItemLedgerDoc>.IndexKeys
                    .Ascending(x => x.Account)
                    .Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_account_ts_desc" });
            var itemByTs = new CreateIndexModel<PlayerItemLedgerDoc>(
                Builders<PlayerItemLedgerDoc>.IndexKeys.Descending(x => x.Timestamp),
                new CreateIndexOptions { Name = "ix_ts_desc" });
            await itemLedger.Indexes.CreateManyAsync(new[] { itemByAccount, itemByTs });
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceComponent: player_item_ledger 索引创建警告(可能重复存在),err={e.Message}");
        }
        self.ItemLedger = itemLedger;

        // 订单池可用性哨兵:池来自 Luban TbMergeOrder(静态缓存,进程生命周期内不重读)。空池 = 导表遗漏 /
        // GameConfigBytes 漏拷的部署级故障(快照全空槽、交付全 ServiceUnavailable),静态初始化期只有 Console 告警
        // 不进 NLog 管道,此处补一条框架日志供运维发现。
        if (MergeOrderConfigServer.OrderPool.Length == 0)
        {
            Log.Error("PlayerPropertyServiceComponent: 订单池为空(TbMergeOrder 缺失/空表)——订单快照将全空槽、交付一律 ServiceUnavailable。请检查导表与 GameConfigBytes 部署,修复后需重启进程。");
        }

        // Luban A 类配置回显(用户验收 Stage 1 配置读表是否生效):
        //   EnergyRecoverSoftCap ← global.xlsx id=4 EnergyRecoverCap
        //   EnergyRecoverIntervalMs / PerTick ← global.xlsx id=3 EnergyRecoverSeconds("amount#interval" 复合)
        //   订单池容量 / 刷新节律 ← MergeOrderConfigServer.ActiveOrders/OrderRefreshIntervalSec(读 global id=1/id=2)
        Log.Info($"PlayerPropertyServiceComponent 初始化完成,玩家属性账本集合句柄已绑定(players + player_attr_ledger);" +
                 $"初始值[coin={self.CoinInitial} diamond={self.DiamondInitial} stamina={self.StaminaInitial}]," +
                 $"上界[coin={self.CoinUpperBound} diamond={self.DiamondUpperBound} stamina={self.StaminaUpperBound}];" +
                 $"Luban A类[EnergyRecoverSoftCap={self.EnergyRecoverSoftCap} EnergyRecoverIntervalMs={self.EnergyRecoverIntervalMs} EnergyRecoverPerTick={self.EnergyRecoverPerTick}" +
                 $" OrderCount={MergeOrderConfigServer.ActiveOrders} OrderRefreshIntervalSec={MergeOrderConfigServer.OrderRefreshIntervalSec}].");
    }
}

public sealed class PlayerPropertyServiceComponentDestroySystem : DestroySystem<PlayerPropertyServiceComponent>
{
    protected override void Destroy(PlayerPropertyServiceComponent self)
    {
        self.Players = null;
        self.AttrLedger = null;
        self.ItemLedger = null;
    }
}
