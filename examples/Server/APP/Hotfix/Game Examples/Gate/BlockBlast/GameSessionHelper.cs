using System.Collections.Generic;
using GameLogic.BlockBlast;
using GameLogic.BlockBlast.Algorithms;
using GameLogic.BlockBlast.Core;

namespace Fantasy;

/// <summary>
/// Block Blast 单局权威逻辑(运行在 Gate Scene 进程内)。
/// 服务端是发牌唯一事实源:发牌器逐局实例 + portable PRNG(seed 服务端签发)+ persistence=null。
///
/// 发牌节律与 GenCoreDeterminismHarness 同口径(权威性回归基线):
///   - 候选以 3 个一批(trio)发放;落子消耗对应候选(置 0);
///   - 整批 3 个全消耗后,先 AddWeight(本批算法) 推进调度态,再 OfferTrio 续发下一批;
///   - 首批走 OfferTrio(空盘, score 0),与之后补批同口径。
/// 这样服务端权威生成的 trio 序列与既有 golden trace 逐位吻合。
/// </summary>
public static class GameSessionHelper
{
    /// <summary>
    /// 建局初始化:按 seed 建逐局发牌器(portable PRNG + 不持久),发首批 trio。
    /// weightcfg 与 harness 同源(DefaultWeightConfig),确保生产路径与权威性回归基线同口径。
    /// </summary>
    public static void Init(GameSession session, long seed)
    {
        session.Seed = seed;
        session.Step = 0;
        session.Score = 0;
        session.LastTrioAlgo = AlgorithmKind.RandomNoDie;
        session.Board = new BinaryBoard();

        var cfg = GenCoreDeterminismHarness.DefaultWeightConfig();
        IRandomSource rng = new XorShift128PlusRng(seed);
        // persistence 省略 = null:逐局发牌态不落客户端存储(服务端权威逐局态)。
        var dyn = new DynamicWeightDiff(rng);
        dyn.ForceAlgorithm = null;
        dyn.Init(cfg);
        dyn.Reset();
        dyn.BeginGame();
        dyn.LastAlgo = null;
        dyn.LastTierId = null;
        session.Generator = dyn;

        session.CandidateQueue.Clear();
        RefillTrio(session);
    }

    /// <summary>
    /// 落子裁决 + 推进。返回裁决结果码;副作用写入 session(棋盘/分数/步号/候选/发牌器态)。
    /// out 参数回带本步增量(消除行列数 / 本步补入的新候选 shapeId,未补为 -1),供响应回带。
    /// 仅 baseStep == session.Step 时执行;调用方负责 &lt; / &gt; 幂等分支(见 handler)。
    /// </summary>
    public static PlaceResultCode Place(GameSession session, int candidateIndex, int posX, int posY,
        out int eliminatedLines, out int newCandidate)
    {
        eliminatedLines = 0;
        newCandidate = -1;

        // 候选槽合法性:索引在界内且非空(0 表示已消耗/空槽)。
        if (candidateIndex < 0 || candidateIndex >= session.CandidateQueue.Count)
        {
            return PlaceResultCode.IllegalPlacement;
        }
        int shapeId = session.CandidateQueue[candidateIndex];
        if (shapeId <= 0)
        {
            return PlaceResultCode.IllegalPlacement;
        }

        // 落点合法性:服务端用自己权威持有的 shapeId 校验,客户端不传形状。
        var pos = new Vec2Int(posX, posY);
        if (!session.Board.CanPutBlock(shapeId, pos))
        {
            return PlaceResultCode.IllegalPlacement;
        }

        int placedCells = BlockShapeMap.GetCellCount(shapeId);

        // 落子。
        session.Board.PutBlock(shapeId, pos);
        session.CandidateQueue[candidateIndex] = 0; // 消耗该候选

        // 消除 + 计分(BlockScoring = 计分单一信息源)。
        var clear = session.Board.CanClearRowCols(true);
        int rows = clear.Rows.Count;
        int cols = clear.Cols.Count;
        int lines = rows + cols;
        int clearedCells = rows * BinaryBoard.ColCount + cols * BinaryBoard.RowCount - rows * cols;
        int placeScore = BlockScoring.PlacementScore(placedCells);
        int clearScore = lines > 0 ? BlockScoring.ClearScore(clearedCells, lines) : 0;
        session.Score += placeScore + clearScore;
        eliminatedLines = lines;

        // 落子后权重反馈:每次落子都用本批算法推进 dynamicWeight(与 golden 同口径,非按批)。
        session.Generator.AddWeight(session.LastTrioAlgo);

        // 整批消耗完才续发下一批(三候选皆空)。
        if (AllConsumed(session.CandidateQueue))
        {
            newCandidate = RefillTrio(session);
        }

        session.Step++;
        return PlaceResultCode.StepAdvanced;
    }

    /// <summary>把生成器状态向量装进协议消息(候选队列 + 跨手累积调度态)。</summary>
    public static BlockGenState BuildGenState(GameSession session)
    {
        // 跨手累积态(含 internal 暴露的 pre / refillIndex)在 Entity 程序集读取后回带。
        var view = GameSessionGenStateView.From(session.Generator);
        var state = BlockGenState.Create();
        state.CandidateQueue.Clear();
        for (int i = 0; i < session.CandidateQueue.Count; i++)
        {
            state.CandidateQueue.Add(session.CandidateQueue[i]);
        }
        state.DynamicWeight = view.DynamicWeight;
        state.PreDynamicWeight = view.PreDynamicWeight;
        state.RefillIndex = view.RefillIndex;
        state.BcInWindow = view.BcInWindow;
        state.BcCooldown = view.BcCooldown;
        return state;
    }

    /// <summary>把权威棋盘 8 行位掩码写进列表(协议 Board 字段)。</summary>
    public static void FillBoard(GameSession session, List<int> dst)
    {
        dst.Clear();
        for (int r = 0; r < BinaryBoard.RowCount; r++)
        {
            dst.Add(session.Board.RowBinary[r]);
        }
    }

    /// <summary>把候选队列写进列表(协议 CandidateQueue 字段)。</summary>
    public static void FillCandidateQueue(GameSession session, List<int> dst)
    {
        dst.Clear();
        for (int i = 0; i < session.CandidateQueue.Count; i++)
        {
            dst.Add(session.CandidateQueue[i]);
        }
    }

    /// <summary>
    /// 走真实发牌路径补满 3 候选,记录本批算法(存逐局 session.LastTrioAlgo)。
    /// 返回队首候选 shapeId(供"本步补入"回带)。
    /// </summary>
    private static int RefillTrio(GameSession session)
    {
        var offer = session.Generator.OfferTrio(session.Board, session.Score);
        session.CandidateQueue.Clear();
        for (int i = 0; i < 3; i++)
        {
            session.CandidateQueue.Add(offer.Ids[i]);
        }
        session.LastTrioAlgo = offer.Algo;
        return session.CandidateQueue.Count > 0 ? session.CandidateQueue[0] : -1;
    }

    private static bool AllConsumed(List<int> queue)
    {
        for (int i = 0; i < queue.Count; i++)
        {
            if (queue[i] > 0) return false;
        }
        return true;
    }
}
