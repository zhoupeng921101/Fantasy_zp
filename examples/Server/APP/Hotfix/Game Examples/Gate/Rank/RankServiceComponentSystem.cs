using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 排行榜服务端组件初始化:绑定原生 MongoDB 集合句柄、建排序索引、载入榜定义缓存、首次启动播种榜定义。
/// 取最优的并发原子性依赖 rank_score 的 _id 复合唯一键(MongoDB 主键天然唯一)与条件更新,见 RankDecisionHelper。
/// 设计基线:design-docs/31-rank-server.md §二/§五。
/// </summary>
public sealed class RankServiceComponentAwakeSystem : AwakeSystem<RankServiceComponent>
{
    /// <summary>
    /// 服务端权威榜定义表(单一来源)。每条的入榜要求/入榜上限/展示上限与客户端 rank.TbRank 同榜 id 口径一致(SV12):
    ///   - RankId          ← rank.TbRank.Id           (榜 id)
    ///   - EnterCondition  ← rank.TbRank.RankCondition (入榜要求/最低入榜分)
    ///   - RankCountMax    ← rank.TbRank.RankCountMax  (入榜上限/参与排名名额)
    ///   - ShowCountMax    ← rank.TbRank.ShowCountMax  (展示上限/查榜返回条数)
    /// 客户端 rank.TbRank 数值改动时,改本表对应行即同源更新;启动 ReconcileRankDefs 会把改动写入已存 MongoDB 文档(消除漂移)。
    /// 服务端工程无 Luban 集成(无 TbXxx/.bytes 加载链,同设计 30 兑换码权威配置已移出 Luban、改 MongoDB 文档的先例),
    /// 故榜定义在服务端以本声明表为权威源、reconcile 进 MongoDB,不建 Luban→服务端导出路径(设计 31 O4「静态配置够用」)。
    /// 奖励/结算字段(reward/valid_type/mail)本增量不消费(不结算,设计 31 读前必看 第 4 条),故不入本表。
    /// </summary>
    private static readonly IReadOnlyList<RankDefDoc> AuthoritativeDefs = new List<RankDefDoc>
    {
        // 榜 1:周榜口径(客户端 BoardWeekly)。入榜要求 100。
        new RankDefDoc { RankId = 1, EnterCondition = 100, RankCountMax = 100, ShowCountMax = 50 },
        // 榜 2:总榜口径(客户端 BoardAlways)。入榜要求 0(全部成绩可进)。
        new RankDefDoc { RankId = 2, EnterCondition = 0, RankCountMax = 100, ShowCountMax = 50 },
        // 榜 9001:小上限测试榜。入榜要求 100、入榜上限 3、展示上限 2,用于少量账号下验 SV6 截断。生产部署可删此榜不影响逻辑。
        new RankDefDoc { RankId = 9001, EnterCondition = 100, RankCountMax = 3, ShowCountMax = 2 }
    };

    protected override void Awake(RankServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    private static async FTask Init(RankServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):全服分数无法持久,上报/查榜会返「服务不可用」。
            // 对应交接区 BLOCKED-环境:逻辑就绪、运行依赖外部 MongoDB。属预期环境条件,用 Warning 不用 Error。
            Log.Warning("RankServiceComponent: MongoDB 实例不可用,排行榜上报/查榜将返回 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            return;
        }

        var scores = mongoDatabase.GetCollection<RankScoreDoc>("rank_score");
        self.Scores = scores;
        var defs = mongoDatabase.GetCollection<RankDefDoc>("rank_def");

        // 查榜按 (RankId 升, BestScore 降, AchievedUnixMs 升) 索引取前 N:
        // 与排序口径(分数降序 + 同分达到时间升序, SV4)一致,避免全量加载到内存再排(SV6 大榜性能, 设计 31 §五)。
        await CreateScoreIndex(scores);

        // 启动对账榜定义:按权威表 upsert + $set 三字段,使权威表数值改动写入已存文档(同源一致, SV12)。
        await ReconcileRankDefs(defs);

        // 载入榜定义到内存缓存(只读裁决用,全服分数权威始终走 MongoDB 原子操作)。
        await ReloadCache(self, defs);

        Log.Info($"RankServiceComponent 初始化完成,榜定义缓存条目数={self.DefCache.Count}");
    }

    /// <summary>
    /// 建全服分数排序索引:(RankId 升序, BestScore 降序, AchievedUnixMs 升序)。
    /// 复合索引贴合查榜的「按榜取 + 分数降序 + 同分早者靠前 + 取前 N」访问模式。
    /// </summary>
    private static async FTask CreateScoreIndex(IMongoCollection<RankScoreDoc> scores)
    {
        var keys = Builders<RankScoreDoc>.IndexKeys
            .Ascending(x => x.RankId)
            .Descending(x => x.BestScore)
            .Ascending(x => x.AchievedUnixMs);
        await scores.Indexes.CreateOneAsync(new CreateIndexModel<RankScoreDoc>(keys));
    }

    /// <summary>
    /// 重新载入榜定义缓存。供启动与(未来)运营热改后刷新。
    /// </summary>
    public static async FTask ReloadCache(RankServiceComponent self, IMongoCollection<RankDefDoc> defs)
    {
        self.DefCache.Clear();
        var all = await defs.Find(FilterDefinition<RankDefDoc>.Empty).ToListAsync();
        foreach (var doc in all)
        {
            self.DefCache[doc.RankId] = doc;
        }
    }

    /// <summary>
    /// 启动对账榜定义:以 AuthoritativeDefs(服务端权威源)为准,把每条按 _id(RankId)upsert 进 rank_def——
    /// 文档不存在则建,存在则 $set 三字段(入榜要求/入榜上限/展示上限)为权威值。
    /// 这使「权威表数值改动(随客户端 rank.TbRank 同源更新)」在下次启动写入已存 MongoDB 文档,消除源变更后两端漂移(SV12)。
    /// 幂等且并发安全:upsert 按 _id 定位,无「插入 vs 更新」分叉竞态;多 Gate 并发首启 / 重启都写同一权威值,结果收敛一致
    ///   (不做先读后写的存在性预检——那是 check-then-act 竞态)。
    /// 注:不消费奖励/结算字段(本表不含),故 reconcile 只覆盖榜级配置三字段,不触碰其他(运营若另存非配置字段不受影响)。
    /// </summary>
    private static async FTask ReconcileRankDefs(IMongoCollection<RankDefDoc> defs)
    {
        foreach (var def in AuthoritativeDefs)
        {
            var filter = Builders<RankDefDoc>.Filter.Eq(x => x.RankId, def.RankId);
            // $set 三个榜级配置字段为权威值;upsert 时按 _id=RankId 建新文档。
            // 权威值变化(改 AuthoritativeDefs)→ 已存文档随之刷新;无变化 → 写回同值,无副作用。
            var update = Builders<RankDefDoc>.Update
                .Set(x => x.EnterCondition, def.EnterCondition)
                .Set(x => x.RankCountMax, def.RankCountMax)
                .Set(x => x.ShowCountMax, def.ShowCountMax);
            var options = new UpdateOptions { IsUpsert = true };
            await defs.UpdateOneAsync(filter, update, options);
        }
    }
}

public sealed class RankServiceComponentDestroySystem : DestroySystem<RankServiceComponent>
{
    protected override void Destroy(RankServiceComponent self)
    {
        self.DefCache.Clear();
        self.Scores = null;
    }
}
