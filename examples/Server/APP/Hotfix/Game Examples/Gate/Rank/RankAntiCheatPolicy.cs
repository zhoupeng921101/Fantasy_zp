using System.Collections.Concurrent;
using Fantasy.Helper;

namespace Fantasy;

/// <summary>
/// 排行榜服务端反作弊裁决(提交成绩前的拦截层,在入榜要求过滤之后、原子取最优之前)。
/// 拦截三个维度:
///   1. 绝对上限:超过 ScoreAbsoluteCeiling 的分数视为客户端伪造/改值,直接拒。
///   2. 频率:同账号同榜两次提交间隔 &lt; MinSubmitIntervalMs 视为脚本刷分,直接拒。
///      上次提交时间戳保存在进程内字典(进程重启清零,攻击者重启服务端的成本远高于刷分收益)。
///   3. 跃升幅度:新分相对历史最佳跃升过大(同时超过倍率上限和绝对增量上限)视为异常。
///      首次提交(无历史)不走此分支,由绝对上限兜底。
/// 任一维度拒绝即返回 Rejected + 拒因字符串(用于 Log.Warning);全部通过返回 Accepted。
/// 阈值为 dev 级静态常量,不需要运营动态调参(若未来需要,可平迁到 RankDefDoc 按榜 id 微调)。
/// </summary>
public static class RankAntiCheatPolicy
{
    /// <summary>分数绝对上限。高于此值视为伪造(rank_condition 同量级 long,留充裕余量)。</summary>
    public const long ScoreAbsoluteCeiling = 10_000_000L;

    /// <summary>同账号同榜两次提交的最小间隔(毫秒)。短于此判定脚本刷分。</summary>
    public const long MinSubmitIntervalMs = 1000L;

    /// <summary>跃升倍率上限:新分 / 历史最佳 超过此倍率才进入跃升异常候选。</summary>
    public const double MaxImprovementRatio = 10.0;

    /// <summary>跃升绝对增量上限:新分 - 历史最佳 超过此值才进入跃升异常候选。</summary>
    public const long MaxImprovementAbs = 100_000L;

    /// <summary>裁决结果。</summary>
    public enum Verdict
    {
        Accepted = 0,
        RejectedAbsoluteCeiling = 1,
        RejectedFrequency = 2,
        RejectedImprovementSpike = 3,
    }

    /// <summary>
    /// 评估一次上报是否放行。已存最佳 0 视为无历史(跃升判定跳过)。
    /// 同时返回判定时刻(nowMs),供调用方在 Accepted 分支写回频率追踪表。
    /// </summary>
    public static Verdict Evaluate(
        ConcurrentDictionary<string, long> lastSubmitAtMs,
        string account, int rankId, long score, long existingBest, long nowMs)
    {
        // 1. 绝对上限。首次和重报都走这条,首次伪造直接挡。
        if (score > ScoreAbsoluteCeiling)
        {
            return Verdict.RejectedAbsoluteCeiling;
        }

        // 2. 频率。键和分数存储 _id 同口径(account|rankId)。
        var key = MakeKey(account, rankId);
        if (lastSubmitAtMs.TryGetValue(key, out var prev))
        {
            if (nowMs - prev < MinSubmitIntervalMs)
            {
                return Verdict.RejectedFrequency;
            }
        }

        // 3. 跃升异常(仅在有历史最佳时)。倍率与绝对增量同时超阈才拒,避免误杀正常进步。
        if (existingBest > 0)
        {
            var ratio = (double)score / existingBest;
            var delta = score - existingBest;
            if (ratio > MaxImprovementRatio && delta > MaxImprovementAbs)
            {
                return Verdict.RejectedImprovementSpike;
            }
        }

        return Verdict.Accepted;
    }

    /// <summary>记录一次成功穿过反作弊的提交时刻(供下次频率判定)。</summary>
    public static void RecordSubmit(
        ConcurrentDictionary<string, long> lastSubmitAtMs, string account, int rankId, long nowMs)
    {
        var key = MakeKey(account, rankId);
        lastSubmitAtMs[key] = nowMs;
    }

    /// <summary>频率表键:与 RankScoreDoc._id 同口径"{account}|{rankId}"。</summary>
    private static string MakeKey(string account, int rankId) => $"{account}|{rankId}";
}
