using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
using GameConfig.reward;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 活动系统服务端组件初始化:绑定原生 MongoDB 集合句柄、对账活动配置、载入配置缓存。
/// 周期幂等的并发原子性依赖 activity_progress 的 _id 复合唯一键 + FindOneAndUpdate 条件写,见 ActivityEvalHelper。
/// 服务端工程无 Luban 集成(无 TbXxx/.bytes 加载链,同设计 30/31/32 先例),
/// 故活动配置在服务端以本声明表为权威源、reconcile 进 MongoDB,不建 Luban→服务端导出路径(设计 39 §3.1 旁注)。
/// 设计基线:design-docs/39-activity-server.md §3.1 / §3.2。
/// </summary>
public sealed class ActivityServiceComponentAwakeSystem : AwakeSystem<ActivityServiceComponent>
{
    /// <summary>
    /// 服务端权威活动配置表(单一来源)。
    /// 字段口径(服务端权威,无 Luban 同源):
    ///   - ActivityId    ← activity.xlsx activity_id
    ///   - Type          ← activity.xlsx type(1=Login,本子单仅接此)
    ///   - Cycle         ← activity.xlsx cycle(1=Daily 跨日重置 / 3=OneShot 永发一次性)
    ///   - Target        ← activity.xlsx target(达标阈值)
    ///   - Rewards       ← activity.xlsx reward(内联奖励条目列表;空列表 = 仅记已发不投邮件)
    ///   - 邮件字段(展开 mail_def):Sender(枚举)/ Title / Content(真实文本)/ ExpireDays
    ///   - StartAtMs=0 / EndAtMs=0(永远开放)
    /// 行 1 = 每日登录奖(Tier 4 第 1 子单 PASS 基线,设计 39)。
    /// 行 2 = EVENT 头像解锁活动(Tier 4 第 2 子单,设计 40):累计登录 7 次永久解锁限定头像 avt_star。
    ///        Rewards 内联 EVENT 头像解锁道具 30101 × 1(客户端段解析 UseEffect=5 EVENT 消费)。
    ///        Cycle=OneShot 一次性永发(达标后 LastClaimedCycleKey=1 永远 ≥ 1,沿 §3.3)。
    /// 后续 N 套活动 = 加新条 + 必要时在 ActivityEvalHelper 加新 Type 分支触发钩子(设计 39 §3.5)。
    /// 生产可直接以本表运营(运营改 textId / Reward id 改本声明 + 重启 → ReconcileDefs upsert 写入已存 MongoDB 文档)。
    /// </summary>
    private static readonly IReadOnlyList<ActivityDefDoc> AuthoritativeDefs = new List<ActivityDefDoc>
    {
        new ActivityDefDoc
        {
            ActivityId = 1,
            Type = 1,            // Login
            Cycle = 1,           // Daily
            Target = 1,          // 登录一次即达标
            Rewards = Item(30002, 2),   // 每日登录奖:内联道具样例(生产按运营口径替换)
            Sender = MailSenderType.System,
            Title = "每日登录奖励",
            Content = "感谢每日登录，奖励请查收。",
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        },
        new ActivityDefDoc
        {
            ActivityId = 2,
            Type = 1,             // Login(每次登录 +1)
            Cycle = 3,            // OneShot(永发一次性)
            Target = 7,           // 累计登录 7 次达标
            Rewards = Item(30101, 1),   // EVENT 头像解锁道具 30101 × 1(客户端段解析 UseEffect=5 EVENT 消费)
            Sender = MailSenderType.System,
            Title = "累计登录奖励",
            Content = "累计登录达标，奖励请查收。",
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        },
        // ── Tier 4 第 3 子单(设计 43)新增两套累计登录类活动,验证「同 type=Login 节律支撑多活动并存」架构能力 ──
        // 活动 3:累计 7 天登录大奖(OneShot 永发一次性)。
        //   与活动 2(EVENT 头像 OneShot/target=7)同周期、同阈值但 reward 不同 → 第 7 次登录玩家邮箱同时 +2 封;
        //   复合主键 {account}_{activityId} 天然隔离(§3.2),各自独立抢占 OneShot key=1 互不干涉。
        new ActivityDefDoc
        {
            ActivityId = 3,
            Type = 1,             // Login
            Cycle = 3,            // OneShot 永发一次性
            Target = 7,           // 累计 7 次登录达标
            Rewards = Item(30002, 1),   // 活动 3 大奖:内联道具样例(生产按运营口径替换)
            Sender = MailSenderType.System,
            Title = "累计登录大奖",
            Content = "累计登录达标，大奖请查收。",
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        },
        // 活动 4:周累计 5 天登录周奖(Weekly 每周一 0:00 UTC 重置)。
        //   引入第三种 cycle 进同登录钩子:Daily(活动 1)+ OneShot(活动 2/3)+ Weekly(活动 4)并存正确,
        //   且 Counter 跨周清零由 ActivityEvalHelper.Increment 借力 LastUpdatedAt + ComputeCurrentCycleKey 反推识别
        //   「上次 +1 与本次 +1 不在同一 Weekly 周期」实现(零 schema 字段加,沿设计 39 §3.2 守不变量)。
        new ActivityDefDoc
        {
            ActivityId = 4,
            Type = 1,             // Login
            Cycle = 2,            // Weekly 每周一 0:00 UTC 重置
            Target = 5,           // 本周累计 5 次登录达标
            Rewards = new List<RewardEntryDoc>   // 活动 4 周奖:多条道具全发样例(生产按运营口径替换)
            {
                new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30001, Amount = 1 },
                new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30003, Amount = 1 }
            },
            Sender = MailSenderType.System,
            Title = "每周登录奖励",
            Content = "本周登录达标，奖励请查收。",
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        },
        // ── Tier 4 第 4 子单(设计 47)新增 Cumulative 节律首套样例 ──────────────────────────────
        // 活动 5:累计游戏 100 局大奖(Cumulative 节律 + OneShot 永发一次性)。
        //   首次 Type=Cumulative(=2)活动:客户端业务方(下一刀 GameOver hook 接入)调 C2G_ActivityIncrement(5, 1)
        //   推累计进度 → handler 校验 type=Cumulative 通过 → 调 ActivityProgressService.Increment → 沿用既有
        //   ActivityEvalHelper.Increment + EvaluateAndClaim 编排(counter 累加 + 抢占周期键 + 发邮件,设计 39 §3.4 流程零改)。
        //   与 Login 类活动(1/2/3/4)节律入口完全独立 — Login 走登录钩子遍历 type=1、Cumulative 走 RPC + service 入口遍历 type=2,
        //   两路 type 过滤独立、复合主键 {account}_5 独立(§3.2 + 设计 47 §3.1),零回归既有 4 套活动行为。
        //   奖励内联道具样例;邮件文案复用占位 textId,
        //   运营后续可改 textId / Rewards / Target 任一字段后重启 → ReconcileDefs 写入已存 MongoDB 文档。
        new ActivityDefDoc
        {
            ActivityId = 5,
            Type = 2,             // Cumulative(本子单首次启用此 type;handler + service 双层校验仅放行此 type 走 RPC 路径)
            Cycle = 3,            // OneShot 永发一次性(累计 100 局后 LastClaimedCycleKey=1,永不重发)
            Target = 100,         // 累计游戏 100 局达标(中期目标;运营可调,SV4 / SV10 不依赖具体数值)
            Rewards = Item(30002, 1),   // 累计 100 局大奖:内联道具样例(生产按运营口径替换)
            Sender = MailSenderType.System,
            Title = "游戏局数奖励",
            Content = "累计游戏达标，奖励请查收。",
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        }
    };

    /// <summary>构造单条道具奖励的内联列表(活动奖励验证样例用,生产按运营口径替换)。</summary>
    private static List<RewardEntryDoc> Item(int itemId, int amount)
    {
        return new List<RewardEntryDoc>
        {
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = itemId, Amount = amount }
        };
    }

    protected override void Awake(ActivityServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建(同 35/32/33/37 范式)。
        Init(self).Coroutine();
    }

    private static async FTask Init(ActivityServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):活动达标判定 / 进度持久无法运行,登录触发钩子静默跳过。
            // 对应交接区 BLOCKED-环境:逻辑就绪、运行依赖外部 MongoDB。属预期环境条件,用 Warning 不用 Error。
            Log.Warning("ActivityServiceComponent: MongoDB 实例不可用,活动达标判定将静默跳过。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            return;
        }

        var defs = mongoDatabase.GetCollection<ActivityDefDoc>("activity_def");
        self.Defs = defs;
        self.Progress = mongoDatabase.GetCollection<ActivityProgressDoc>("activity_progress");

        // 启动对账活动配置:按权威表 upsert + $set,使权威表数值改动写入已存文档(同源一致,设计 39 §3.1)。
        // 幂等且并发安全:upsert 按 _id=ActivityId 定位,无「插入 vs 更新」分叉竞态;多 Gate 并发首启 / 重启都收敛一致
        //   (不做先读后写的存在性预检——那是 check-then-act 竞态,同 33 ReconcileRankDefs 范式)。
        await ReconcileDefs(defs);

        // 载入配置缓存(只读裁决用,进度判定的权威始终走 MongoDB 原子操作)。
        await ReloadCache(self);

        Log.Info($"ActivityServiceComponent 初始化完成,活动配置缓存条目数={self.DefCache.Count}(activity_id=1 每日登录奖 Daily + activity_id=2 EVENT 头像 OneShot + activity_id=3 累计 7 天大奖 OneShot + activity_id=4 周累计 5 天周奖 Weekly + activity_id=5 累计游戏 100 局 Cumulative OneShot)。");
    }

    /// <summary>
    /// 启动对账活动配置:以 AuthoritativeDefs(服务端权威源)为准,把每条按 _id(ActivityId)upsert 进 activity_def。
    /// 文档不存在则建,存在则 $set 全部配置字段为权威值。同 33 ReconcileRankDefs 范式。
    /// 注:reconcile 覆盖配置字段(本表声明的全部),不触碰运行期写的 activity_progress(那是玩家进度,非配置)。
    /// </summary>
    private static async FTask ReconcileDefs(IMongoCollection<ActivityDefDoc> defs)
    {
        foreach (var def in AuthoritativeDefs)
        {
            var filter = Builders<ActivityDefDoc>.Filter.Eq(x => x.ActivityId, def.ActivityId);
            var update = Builders<ActivityDefDoc>.Update
                .Set(x => x.Type, def.Type)
                .Set(x => x.Cycle, def.Cycle)
                .Set(x => x.Target, def.Target)
                .Set(x => x.Rewards, def.Rewards)
                .Set(x => x.Sender, def.Sender)
                .Set(x => x.Title, def.Title)
                .Set(x => x.Content, def.Content)
                .Set(x => x.ExpireDays, def.ExpireDays)
                .Set(x => x.StartAtMs, def.StartAtMs)
                .Set(x => x.EndAtMs, def.EndAtMs);
            var options = new UpdateOptions { IsUpsert = true };
            await defs.UpdateOneAsync(filter, update, options);
        }
    }

    /// <summary>重新载入活动配置缓存。供启动与(未来)运营热改后刷新。</summary>
    public static async FTask ReloadCache(ActivityServiceComponent self)
    {
        if (self.Defs == null)
        {
            return;
        }
        self.DefCache.Clear();
        var all = await self.Defs.Find(FilterDefinition<ActivityDefDoc>.Empty).ToListAsync();
        foreach (var doc in all)
        {
            self.DefCache[doc.ActivityId] = doc;
        }
    }
}

public sealed class ActivityServiceComponentDestroySystem : DestroySystem<ActivityServiceComponent>
{
    protected override void Destroy(ActivityServiceComponent self)
    {
        self.DefCache.Clear();
        self.Defs = null;
        self.Progress = null;
    }
}
