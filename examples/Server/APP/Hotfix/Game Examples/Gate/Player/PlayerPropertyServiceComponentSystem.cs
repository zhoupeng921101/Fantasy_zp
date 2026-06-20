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
        if (self.CoinUpperBound < 0 || self.DiamondUpperBound < 0 || self.StaminaUpperBound < 0)
        {
            return false;
        }

        // 留出 delta 加法不溢出的安全余量:上界不超过 long.MaxValue 的一半,
        // delta 范围 = [-上界, +上界],余额 + delta 在 [-上界, 2 * 上界] 内,
        // 余额 + delta 比较保证不会因 long 溢出绕过 MongoDB 端条件过滤(§3.4 + §5.3 风险表)。
        const long maxSafeUpperBound = long.MaxValue / 2;
        if (self.CoinUpperBound > maxSafeUpperBound || self.DiamondUpperBound > maxSafeUpperBound || self.StaminaUpperBound > maxSafeUpperBound)
        {
            return false;
        }

        if (self.CoinInitial < 0 || self.CoinInitial > self.CoinUpperBound)
        {
            return false;
        }

        if (self.DiamondInitial < 0 || self.DiamondInitial > self.DiamondUpperBound)
        {
            return false;
        }

        if (self.StaminaInitial < 0 || self.StaminaInitial > self.StaminaUpperBound)
        {
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
        Log.Info($"PlayerPropertyServiceComponent 初始化完成,玩家属性账本集合句柄已绑定(players);" +
                 $"初始值[coin={self.CoinInitial} diamond={self.DiamondInitial} stamina={self.StaminaInitial}]," +
                 $"上界[coin={self.CoinUpperBound} diamond={self.DiamondUpperBound} stamina={self.StaminaUpperBound}].");
        await FTask.CompletedTask;
    }
}

public sealed class PlayerPropertyServiceComponentDestroySystem : DestroySystem<PlayerPropertyServiceComponent>
{
    protected override void Destroy(PlayerPropertyServiceComponent self)
    {
        self.Players = null;
    }
}
