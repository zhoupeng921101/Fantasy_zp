using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
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
    ///   - NameTextId    ← activity.xlsx name_text_id
    ///   - DescTextId    ← activity.xlsx desc_text_id
    ///   - Type          ← activity.xlsx type(1=Login,本子单仅接此)
    ///   - Cycle         ← activity.xlsx cycle(1=Daily 跨日重置 / 3=OneShot 永发一次性)
    ///   - Target        ← activity.xlsx target(达标阈值)
    ///   - Reward        ← activity.xlsx reward(礼包随机库 id;指向 gift_pool 的 Index)
    ///   - 邮件字段(展开 mail_def):SenderTextId / TitleTextId / ContentTextId / ExpireDays
    ///     验收期 textId 未必有多语言条目,客户端展示落空值,不影响 SV 真往返(看 mails 集合 + 抽奖落地)。
    ///   - StartAtMs=0 / EndAtMs=0(永远开放)
    /// 行 1 = 每日登录奖(Tier 4 第 1 子单 PASS 基线,设计 39)。
    /// 行 2 = EVENT 头像解锁活动(Tier 4 第 2 子单,设计 40):累计登录 7 次永久解锁限定头像 avt_star。
    ///        Reward=6101 EVENT 礼包(MailServiceComponentSystem.GiftPoolSeeds 已注册,单项必中 ItemId=30101 × 1)。
    ///        Cycle=OneShot 一次性永发(达标后 LastClaimedCycleKey=1 永远 ≥ 1,沿 §3.3)。
    /// 后续 N 套活动 = 加新条 + 必要时在 ActivityEvalHelper 加新 Type 分支触发钩子(设计 39 §3.5)。
    /// 生产可直接以本表运营(运营改 textId / Reward id 改本声明 + 重启 → ReconcileDefs upsert 写入已存 MongoDB 文档)。
    /// </summary>
    private static readonly IReadOnlyList<ActivityDefDoc> AuthoritativeDefs = new List<ActivityDefDoc>
    {
        new ActivityDefDoc
        {
            ActivityId = 1,
            NameTextId = 110730,
            DescTextId = 110731,
            Type = 1,            // Login
            Cycle = 1,           // Daily
            Target = 1,          // 登录一次即达标
            Reward = 1005,       // 礼包随机库 id;1005 与排行榜 2-10 名档复用同库(MailServiceComponentSystem.GiftPoolSeeds 已注册)
            SenderTextId = 110700,
            TitleTextId = 110732,
            ContentTextId = 110733,
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        },
        new ActivityDefDoc
        {
            ActivityId = 2,
            NameTextId = 390003,  // EVENT 活动名 textId(占位,沿 §3.5)
            DescTextId = 390004,  // EVENT 活动描述 textId(占位,沿 §3.5)
            Type = 1,             // Login(每次登录 +1)
            Cycle = 3,            // OneShot(永发一次性)
            Target = 7,           // 累计登录 7 次达标
            Reward = 6101,        // EVENT 礼包 id(GiftPoolSeeds 已注册:Index=6101 → ItemId=30101 × 1, Rate=100 单项必中)
            SenderTextId = 110700,// 沿 mail 系统占位发件人 textId(同 activity 1)
            TitleTextId = 390003, // EVENT 活动结算邮件标题 textId(占位,运营后续配多语言)
            ContentTextId = 390004,
            ExpireDays = 14,
            StartAtMs = 0,
            EndAtMs = 0
        }
    };

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

        Log.Info($"ActivityServiceComponent 初始化完成,活动配置缓存条目数={self.DefCache.Count}(activity_id=1 每日登录奖 + activity_id=2 EVENT 头像解锁活动)。");
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
                .Set(x => x.NameTextId, def.NameTextId)
                .Set(x => x.DescTextId, def.DescTextId)
                .Set(x => x.Type, def.Type)
                .Set(x => x.Cycle, def.Cycle)
                .Set(x => x.Target, def.Target)
                .Set(x => x.Reward, def.Reward)
                .Set(x => x.SenderTextId, def.SenderTextId)
                .Set(x => x.TitleTextId, def.TitleTextId)
                .Set(x => x.ContentTextId, def.ContentTextId)
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
