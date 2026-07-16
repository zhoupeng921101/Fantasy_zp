using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 排行榜裁决核心逻辑(服务端唯一权威)。
/// 上报:查榜定义 → 过滤入榜要求 → 取最优(原子条件写)→ 回当前最佳。
/// 查榜:按 (分数降序 + 同分达到时间升序) 取该榜记录 → 截入榜上限算名次 → 截展示上限回条目 + 算请求者名次。
/// 并发原子性:
///   - 取最优:rank_score 用 FindOneAndUpdate(filter _id 匹配 且 BestScore&lt;新分, $set 新分+时间, upsert) 单次原子。
///     仅严格更高分刷新;并发下低分写不进(无低分覆盖高分, SV7)。
/// 失败/边界分支不抛异常,以结果码回包(SV9/SV10)。
/// 设计基线:design-docs/31-rank-server.md §二/§三/§五。
/// </summary>
public static class RankDecisionHelper
{
    /// <summary>MongoDB 重复键错误码。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>玩家展示名占位前缀:本增量回账号标识占位,客户端有本地昵称则替换(§3.5 注 / O5)。</summary>
    private const string DisplayNamePrefix = "Player_";

    // ── 上报成绩 ──────────────────────────────────────────────

    /// <summary>
    /// 裁决一次上报。account 为服务端从会话取得的设备账号(非客户端自报)。
    /// 返回结果码 + 该账号该榜当前最佳(无成绩为 0)。
    /// </summary>
    public static async FTask<(RankSubmitResultCode resultCode, long bestScore)> Submit(
        RankServiceComponent self, string account, int rankId, long score)
    {
        // 服务未就绪(MongoDB 不可达):返「服务不可用」,不阻断玩法(成绩已在客户端本地, 设计 §四)。
        if (self.Scores == null)
        {
            return (RankSubmitResultCode.ServiceUnavailable, 0L);
        }
        var scores = self.Scores;

        // 1. 榜不存在 → 结果码回包,不崩、不写存储(SV9)。
        if (!self.DefCache.TryGetValue(rankId, out var def))
        {
            return (RankSubmitResultCode.RankNotFound, 0L);
        }

        // 2. 未达入榜要求 → 不写存储、不进榜;回该账号已存最佳(可能为 0)供「距上榜差值」(SV3)。
        //    0 分/负分天然落入此分支(入榜要求 >0 时),不特殊崩(SV10)。
        if (score < def.EnterCondition)
        {
            var existingForBelow = await QueryBestScore(scores, account, rankId);
            return (RankSubmitResultCode.BelowEnterRequirement, existingForBelow);
        }

        // 3. 反作弊裁决(P1):绝对上限 / 频率 / 跃升异常,任一命中 → 拒、不写存储、Log 拒因(account/rankId/score/existing/拒因)。
        //    跃升判定需要历史最佳,先查一次;通过后该 existing 也供「未刷新」分支复用,省一次查询。
        var existing = await QueryBestScore(scores, account, rankId);
        var now = TimeHelper.Now;
        var verdict = RankAntiCheatPolicy.Evaluate(self.AntiCheatLastSubmitAtMs, account, rankId, score, existing, now);
        if (verdict != RankAntiCheatPolicy.Verdict.Accepted)
        {
            Log.Warning($"排行榜上报被反作弊拦截 reason={verdict} account={account} rankId={rankId} score={score} existingBest={existing}");
            return (RankSubmitResultCode.RejectedByAntiCheat, existing);
        }
        // 通过 → 记本次时刻(供后续频率判定);即使后续原子写未刷新,也算「客户端发起过一次合法提交」,频率窗口该推进。
        RankAntiCheatPolicy.RecordSubmit(self.AntiCheatLastSubmitAtMs, account, rankId, now);

        // 4. 取最优:原子条件写(仅当新分严格高于存量才刷新分+时间, SV2/SV7)。
        var key = MakeKey(account, rankId);
        var refreshed = await TryRefreshBest(scores, key, account, rankId, score, now);
        if (refreshed)
        {
            // 已刷新最佳:当前最佳 = 本次成绩(SV1/SV2 先低后高分支)。
            return (RankSubmitResultCode.BestRefreshed, score);
        }

        // 5. 未刷新(够入榜要求但不高于已存最佳):回已存最佳(高于/等于本次, SV2 先高后低分支)。
        //    并发场景下该 existing 可能略旧(原子写之间被其他请求顶过),再查一次保正确。
        var best = await QueryBestScore(scores, account, rankId);
        return (RankSubmitResultCode.BestNotRefreshed, best);
    }

    /// <summary>
    /// 取最优的单次原子条件写:FindOneAndUpdate(filter _id=key 且 BestScore&lt;score, $set 新分+时间, upsert)。
    /// 三种情形:
    ///   - 文档不存在 → upsert 按 _id=key 与各 $set 字段建新文档(首次上报, BestScore=score)→ true。
    ///   - 文档存在且 BestScore&lt;score → 原子刷新分+时间 → true。
    ///   - 文档存在且 BestScore&gt;=score → filter 不匹配,upsert 试图按 _id=key 插新文档 → 主键冲突(11000)→ false(未刷新)。
    /// 并发两次上报不同分:都走条件写,只有真更高者写成功,低分被 filter 或主键冲突挡下(SV7 无低分覆盖)。
    /// 注:同分(score==BestScore)走 BestScore&lt;score 不成立 → false,不更新时间,保住先到先得(设计 §3.3)。
    /// </summary>
    private static async FTask<bool> TryRefreshBest(
        IMongoCollection<RankScoreDoc> scores, string key, string account, int rankId, long score, long nowMs)
    {
        var filter = Builders<RankScoreDoc>.Filter.And(
            Builders<RankScoreDoc>.Filter.Eq(x => x.UniqueKey, key),
            Builders<RankScoreDoc>.Filter.Lt(x => x.BestScore, score));
        // upsert 时 SetOnInsert 补齐不可变字段(Account/RankId);Set 写可变字段(分+时间)。
        var update = Builders<RankScoreDoc>.Update
            .SetOnInsert(x => x.Account, account)
            .SetOnInsert(x => x.RankId, rankId)
            .Set(x => x.BestScore, score)
            .Set(x => x.AchievedUnixMs, nowMs);
        var options = new FindOneAndUpdateOptions<RankScoreDoc> { IsUpsert = true };

        try
        {
            await scores.FindOneAndUpdateAsync(filter, update, options);
            return true;
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return false;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <summary>查该账号该榜当前最佳成绩;无记录返 0。</summary>
    private static async FTask<long> QueryBestScore(IMongoCollection<RankScoreDoc> scores, string account, int rankId)
    {
        var filter = Builders<RankScoreDoc>.Filter.Eq(x => x.UniqueKey, MakeKey(account, rankId));
        var doc = await scores.Find(filter).FirstOrDefaultAsync();
        return doc?.BestScore ?? 0L;
    }

    // ── 查榜 ──────────────────────────────────────────────────

    /// <summary>
    /// 查一个榜。account 为服务端从会话取得的设备账号(算「我的名次」用,非客户端自报)。
    /// 返回结果码 + 已排序截展示上限的条目 + 请求者名次(未入榜=0) + 请求者当前最佳。
    /// 排序口径与设计 22 客户端服务层一致:分数降序 + 同分达到时间升序 + 1 起顺序名次(同分各占唯一名次, SV4)。
    /// </summary>
    public static async FTask<(RankQueryResultCode resultCode, List<RankEntryItem> entries, int myRank, long myScore)> Query(
        RankServiceComponent self, string account, int rankId)
    {
        var emptyEntries = new List<RankEntryItem>();

        if (self.Scores == null)
        {
            return (RankQueryResultCode.ServiceUnavailable, emptyEntries, 0, 0L);
        }
        var scores = self.Scores;

        // 榜不存在 → 结果码回包(SV9)。
        if (!self.DefCache.TryGetValue(rankId, out var def))
        {
            return (RankQueryResultCode.RankNotFound, emptyEntries, 0, 0L);
        }

        // 入榜要求过滤 + 排序由 MongoDB 完成(贴合复合索引: RankId 升, BestScore 降, AchievedUnixMs 升)。
        // 入榜上限 RankCountMax 决定参与名次的名额:>0 则只取前 N(查询时刻快照, 设计 §五 名次抖动可接受)。
        var filter = Builders<RankScoreDoc>.Filter.And(
            Builders<RankScoreDoc>.Filter.Eq(x => x.RankId, rankId),
            Builders<RankScoreDoc>.Filter.Gte(x => x.BestScore, def.EnterCondition));
        var sort = Builders<RankScoreDoc>.Sort
            .Descending(x => x.BestScore)
            .Ascending(x => x.AchievedUnixMs);

        var find = scores.Find(filter).Sort(sort);
        if (def.RankCountMax > 0)
        {
            find = find.Limit(def.RankCountMax);
        }
        var ranked = await find.ToListAsync();

        // 回填名次(1 起,顺序名次:同分也各占唯一名次, SV4)+ 找请求者名次。
        var myRank = 0;
        var myScore = 0L;
        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].Account == account)
            {
                myRank = i + 1;
                myScore = ranked[i].BestScore;
                break;
            }
        }

        // 未在参与名次的名额内(无成绩/低于入榜要求/超入榜上限):名次 0,分数照回该账号当前最佳供「距上榜差值」(SV5)。
        if (myRank == 0)
        {
            myScore = await QueryBestScore(scores, account, rankId);
        }

        // 展示上限 ShowCountMax:返回条目截前 N 条(>0 才截, SV6)。条目走对象池 Create(随响应发, 响应 Dispose 归还池)。
        var showCount = def.ShowCountMax > 0 ? def.ShowCountMax : ranked.Count;
        if (showCount > ranked.Count)
        {
            showCount = ranked.Count;
        }
        var entries = new List<RankEntryItem>(showCount);
        // 批量取展示账号的真实昵称(一次 $in 查询;拉榜是热路径,避免逐条查库)。
        var showAccounts = new List<string>(showCount);
        for (var i = 0; i < showCount; i++)
        {
            showAccounts.Add(ranked[i].Account);
        }
        var nicknames = await FetchNicknames(self, showAccounts);
        for (var i = 0; i < showCount; i++)
        {
            var item = RankEntryItem.Create();
            item.Rank = i + 1;
            item.PlayerName = DisplayName(ranked[i].Account, nicknames);
            item.Score = ranked[i].BestScore;
            entries.Add(item);
        }

        return (RankQueryResultCode.Success, entries, myRank, myScore);
    }

    // ── 工具 ──────────────────────────────────────────────────

    /// <summary>全服分数文档 _id 复合键:"{account}|{rankId}"。</summary>
    private static string MakeKey(string account, int rankId) => $"{account}|{rankId}";

    /// <summary>玩家展示名:有真实昵称(改过名, PlayerDoc.Nickname 非空)用昵称,否则回退账号占位。</summary>
    private static string DisplayName(string account, IReadOnlyDictionary<string, string> nicknames)
        => nicknames.TryGetValue(account, out var nick) ? nick : DisplayNamePrefix + account;

    /// <summary>
    /// 批量取展示账号的真实昵称(PlayerDoc.Nickname):一次 $in 查询建 account→Nickname 字典,只放非空昵称
    /// (未改名者不进字典 → DisplayName 回退占位)。拉榜热路径:整批一次查库、不逐条(N 账号 → 1 次有界读)。
    /// DB 不可达 → 返空字典(全回退占位,不阻断拉榜)。
    /// </summary>
    private static async FTask<Dictionary<string, string>> FetchNicknames(RankServiceComponent self, List<string> accounts)
    {
        var map = new Dictionary<string, string>();
        if (accounts.Count == 0)
        {
            return map;
        }
        if (self.Scene.World.Database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            return map;
        }
        var players = mongoDatabase.GetCollection<PlayerDoc>("players");
        var filter = Builders<PlayerDoc>.Filter.In(x => x.AccountId, accounts);
        var docs = await players.Find(filter).ToListAsync();
        foreach (var d in docs)
        {
            if (!string.IsNullOrEmpty(d.Nickname))
            {
                map[d.AccountId] = d.Nickname;
            }
        }
        return map;
    }

    // ── 清档 ──────────────────────────────────────────────────

    /// <summary>
    /// 清档·删除某账号在 rank_score 的全部分数行(按玩家身份 Account 删,非 _id)。
    /// 一个账号每参与一个榜各占一行(_id = "{account}|{rankId}"),故 1:N → DeleteMany。
    /// 删后该账号退出所有榜(查榜不再含其名次)。
    /// **不**触碰 rank_settle:其 _id=RankId,是全服每榜的结算幂等标记(无 Account 字段),
    /// 属全局共享状态——删除会让该榜对所有玩家重复结算,不在 per-player 清档范围。
    /// 幂等:0 匹配(本就未上榜)同样视为成功。返回 true=成功(含本就无行);false=MongoDB 不可达 / 异常。
    /// </summary>
    public static async FTask<bool> ClearByAccount(RankServiceComponent self, string account)
    {
        if (self.Scores == null)
        {
            return false;
        }
        if (string.IsNullOrEmpty(account))
        {
            return true;
        }

        try
        {
            var filter = Builders<RankScoreDoc>.Filter.Eq(x => x.Account, account);
            var result = await self.Scores.DeleteManyAsync(filter);
            Log.Debug($"Rank 清档删除分数 account={account} deletedCount={result.DeletedCount}");
            return true;
        }
        catch (MongoException e)
        {
            Log.Warning($"RankDecisionHelper.ClearByAccount 失败 account={account},err={e.Message}");
            return false;
        }
    }
}
