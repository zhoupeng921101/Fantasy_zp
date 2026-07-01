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
        session.ClearToolInFlight = false; // 对象池复用防残留在途标记
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
    /// 续局重建:从持久 Doc 还原完整权威态 + 发牌器全运行态(RNG 游标 + 调度标量 + LastAlgo/LastTier),
    /// 使续局后续发牌与中断前逐位接续(同游标 + 同标量 → 同后续 trio)。
    ///
    /// 与 <see cref="Init"/> 的关键区别:不调 Reset()/BeginGame()(那会清零调度标量),
    /// 而是 new XorShift128PlusRng(s0,s1) 把游标直接落在中断点 → Init(同配置)→ ImportFullState(全态注入)。
    /// weightcfg 与 Init / harness 同源(DefaultWeightConfig),确保续局后调度判据与中断前同口径。
    /// </summary>
    public static void Rehydrate(GameSession session, GameSessionDoc doc)
    {
        session.PlayerId = doc.PlayerId;
        session.GameId = doc.GameId;
        session.Seed = doc.Seed;
        session.Step = doc.Step;
        session.Score = doc.Score;
        session.LastTrioAlgo = doc.LastTrioAlgo < 0 ? AlgorithmKind.RandomNoDie : (AlgorithmKind)doc.LastTrioAlgo;
        session.ClearToolInFlight = false; // 对象池复用防残留在途标记

        // 还原棋盘 8 行位掩码。
        session.Board = new BinaryBoard();
        for (int r = 0; r < BinaryBoard.RowCount && r < doc.Board.Length; r++)
        {
            session.Board.RowBinary[r] = doc.Board[r];
        }

        // 还原候选队列。
        session.CandidateQueue.Clear();
        for (int i = 0; i < doc.CandidateQueue.Length; i++)
        {
            session.CandidateQueue.Add(doc.CandidateQueue[i]);
        }

        // 还原发牌器全运行态:RNG 游标经 ctor 复位,Init 装配置(不重置标量),ImportFullState 注入标量 + LastAlgo/LastTier。
        var cfg = GenCoreDeterminismHarness.DefaultWeightConfig();
        var rng = new XorShift128PlusRng(unchecked((ulong)doc.RngS0), unchecked((ulong)doc.RngS1));
        var dyn = new DynamicWeightDiff(rng);
        dyn.ForceAlgorithm = null;
        dyn.Init(cfg);
        var full = new DynamicWeightDiff.FullState(
            unchecked((ulong)doc.RngS0), unchecked((ulong)doc.RngS1),
            doc.DynamicWeight, doc.PreDynamicWeight, doc.RefillIndex,
            doc.BcInWindow, doc.BcCooldown, doc.GenLastAlgo, doc.GenLastTierId);
        dyn.ImportFullState(full);
        session.Generator = dyn;
    }

    /// <summary>把当前权威态(盘面 + 分数 + 步号 + 候选 + 发牌器全态)装进持久文档。</summary>
    public static GameSessionDoc BuildDoc(GameSession session)
    {
        var view = GameSessionGenStateView.From(session.Generator);
        var doc = new GameSessionDoc
        {
            PlayerId = session.PlayerId,
            GameId = session.GameId,
            Seed = session.Seed,
            Step = session.Step,
            Score = session.Score,
            LastTrioAlgo = (int)session.LastTrioAlgo,
            RngS0 = view.RngS0,
            RngS1 = view.RngS1,
            DynamicWeight = view.DynamicWeight,
            PreDynamicWeight = view.PreDynamicWeight,
            RefillIndex = view.RefillIndex,
            BcInWindow = view.BcInWindow,
            BcCooldown = view.BcCooldown,
            GenLastAlgo = view.LastAlgo,
            GenLastTierId = view.LastTierId,
        };

        doc.Board = new int[BinaryBoard.RowCount];
        for (int r = 0; r < BinaryBoard.RowCount; r++)
        {
            doc.Board[r] = session.Board.RowBinary[r];
        }

        doc.CandidateQueue = new int[session.CandidateQueue.Count];
        for (int i = 0; i < session.CandidateQueue.Count; i++)
        {
            doc.CandidateQueue[i] = session.CandidateQueue[i];
        }

        return doc;
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

    /// <summary>
    /// 消除道具裁决(服务端权威,设计 49 §3.1):清目标格 (posX,posY) 所在整行整列的全部已占格。
    /// 只清一行一列,不清空全盘、不触发全清判定、不给全清奖(设计 49 §四);不消耗候选、不推进发牌调度、不续发。
    /// 作为一次 board-mutating 动作推进 Step(与落子同一步号轴,供幂等)。返回本次清掉的格数。
    ///
    /// 越界(row/col 不在 0..7)由调用方(handler)先判并回 OutOfRange,此处不再重复越界回退。
    /// 与客户端同源逻辑 BlockGameState.ClearToolRowCol 同口径:清一整行 + 一整列,交叉格只清一次(位掩码天然去重)。
    /// </summary>
    public static int ClearTool(GameSession session, int posX, int posY)
    {
        var board = session.Board;
        int before = CountOccupied(board);

        // 清整行:该行位掩码全清零。
        board.RowBinary[posY] = 0;

        // 清整列:每行清掉目标列对应的那一位(位序与 BinaryBoard 一致:bit (ColCount-col-1))。
        int colClearMask = ~(1 << (BinaryBoard.ColCount - posX - 1)) & BinaryBoard.FullRow;
        for (int r = 0; r < BinaryBoard.RowCount; r++)
        {
            board.RowBinary[r] &= colClearMask;
        }

        int cleared = before - CountOccupied(board);
        session.Step++;
        return cleared;
    }

    /// <summary>统计棋盘已占格数(消除道具清格数派生用)。</summary>
    private static int CountOccupied(BinaryBoard board)
    {
        int count = 0;
        for (int r = 0; r < BinaryBoard.RowCount; r++)
        {
            int bits = board.RowBinary[r] & BinaryBoard.FullRow;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }
        }
        return count;
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
        state.RngS0 = view.RngS0;
        state.RngS1 = view.RngS1;
        state.LastAlgo = view.LastAlgo;
        state.LastTierId = view.LastTierId;
        return state;
    }

    /// <summary>
    /// 终局判定(服务端权威):当前候选队列存在非空候选,且无任一放置顺序能把它们全放下(jam)→ true=本局结束。
    /// 复用生成核心 BinaryBoard.CheckPutAllBlocks(权威性回归基线同口径):
    ///   - 候选全空(理论上不出现,落子后整批消耗会立即续发新批)→ CheckPutAllBlocks 返 true(无候选不阻塞)→ 非终局;
    ///   - 候选非空且全不可放 → CheckPutAllBlocks 返 false → 终局。
    /// 在 Place 续发新批之后调用:判的是落子裁决后玩家实际面对的候选(剩余候选 / 新发整批)。
    /// </summary>
    public static bool IsGameOver(GameSession session)
    {
        var ids = new int[session.CandidateQueue.Count];
        for (int i = 0; i < session.CandidateQueue.Count; i++)
        {
            ids[i] = session.CandidateQueue[i];
        }
        return !session.Board.CheckPutAllBlocks(ids);
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
