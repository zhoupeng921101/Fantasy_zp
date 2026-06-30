using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameLogic.BlockBlast;
using GameLogic.BlockBlast.Algorithms;
using GameLogic.BlockBlast.Core;

namespace BlockBlastGenCore
{
    /// <summary>
    /// 续局忠实性自检(M3b 服务端段·续存正确性核心)。
    ///
    /// 复现服务端续局路径:建局→落 N 子→把发牌器全运行态(RNG 游标 + 5 标量 + LastAlgo/LastTier)
    /// 序列化到一份「Doc」(纯字段快照)→从 Doc 重建发牌器(new XorShift128PlusRng(s0,s1) + Init + ImportFullState)
    /// →续落 M 子。断言:续接段的 trio/盘面/分数与「不中断一气跑完 N+M 子」逐位一致。
    /// 这等价证明 ExportFullState/ImportFullState + XorShift128PlusRng(s0,s1) 游标复位忠实,
    /// 续局后续发牌与中断前逐位接续。
    ///
    /// 落子玩家策略与 ServerCadenceVerify 同口径(逐槽首个可放、落点 GetCanPutPoss 固定序首、卡死清盘),
    /// 使本自检与既有权威性回归同一发牌节律。
    /// </summary>
    internal static class ResumeContinuityVerify
    {
        /// <summary>发牌器全运行态的中立快照(等价服务端 GameSessionDoc 的发牌器字段子集)。</summary>
        private struct Snapshot
        {
            public ulong RngS0, RngS1;
            public int DynamicWeight, PreDynamicWeight, RefillIndex;
            public bool BcInWindow;
            public int BcCooldown;
            public int LastAlgo, LastTierId;
            // 局面态
            public int[] Board;        // 8 行
            public int[] Candidates;   // 候选队列
            public int Step, Score;
            public AlgorithmKind LastTrioAlgo;
        }

        public static int Run(int seed, int n, int m)
        {
            // 参考:一气跑完 N+M 子的完整 trace。
            string golden = RunOneShot(seed, n + m);

            // 续局:跑 N 子 → 序列化 → 重建 → 续 M 子,落与 golden 同格式 trace。
            string resumed = RunResumed(seed, n, m);

            bool identical = string.Equals(golden, resumed, StringComparison.Ordinal);
            if (identical)
            {
                Console.WriteLine($"[resume seed={seed} N={n} M={m}] 续局逐位接续 ✓(与一气跑完 N+M 逐字符一致)");
                return 0;
            }
            int div = FirstDivergenceLine(golden, resumed);
            Console.WriteLine($"[resume seed={seed} N={n} M={m}] 续局发散 ✗  首个发散行={div}");
            DumpAround(golden, resumed, div);
            return 1;
        }

        private static string RunOneShot(int seed, int steps)
        {
            var sb = new StringBuilder(64 * 1024);
            var state = NewGame(seed);
            for (int step = 0; step < steps; step++)
            {
                if (!StepOnce(ref state, sb, step)) break;
            }
            return sb.ToString();
        }

        private static string RunResumed(int seed, int n, int m)
        {
            var sb = new StringBuilder(64 * 1024);
            var state = NewGame(seed);

            // 段一:落 N 子(写入同一 trace)。
            int step = 0;
            for (; step < n; step++)
            {
                if (!StepOnce(ref state, sb, step)) return sb.ToString();
            }

            // 序列化全态到 Doc → 丢弃实例 → 从 Doc 重建(模拟跨进程 / 跨开窗)。
            var snap = Serialize(in state);
            var rebuilt = Deserialize(in snap);

            // 段二:从重建态续落 M 子(同一 trace 续写)。
            for (int k = 0; k < m; k++, step++)
            {
                if (!StepOnce(ref rebuilt, sb, step)) break;
            }
            return sb.ToString();
        }

        // ─── 局面态 + 发牌器封装(等价 GameSession 字段) ───

        private struct GameState
        {
            public DynamicWeightDiff Gen;
            public BinaryBoard Board;
            public int[] Trio;
            public AlgorithmKind LastTrioAlgo;
            public int Score;
            public int Combo;
        }

        /// <summary>等价 GameSessionHelper.Init:建发牌器 + 发首批 trio。</summary>
        private static GameState NewGame(int seed)
        {
            var cfg = GenCoreDeterminismHarness.DefaultWeightConfig();
            IRandomSource rng = new XorShift128PlusRng(seed);
            var dyn = new DynamicWeightDiff(rng);
            dyn.ForceAlgorithm = null;
            dyn.Init(cfg);
            dyn.Reset();
            dyn.BeginGame();
            dyn.LastAlgo = null;
            dyn.LastTierId = null;

            var st = new GameState
            {
                Gen = dyn,
                Board = new BinaryBoard(),
                Trio = new[] { -1, -1, -1 },
                LastTrioAlgo = AlgorithmKind.RandomNoDie,
                Score = 0,
                Combo = 0,
            };

            var offer = dyn.OfferTrio(st.Board, st.Score);
            for (int i = 0; i < 3; i++) st.Trio[i] = offer.Ids[i];
            st.LastTrioAlgo = offer.Algo;
            return st;
        }

        /// <summary>等价 GameSessionHelper.BuildDoc:导出全运行态 + 局面。</summary>
        private static Snapshot Serialize(in GameState st)
        {
            var full = st.Gen.ExportFullState();
            var board = new int[BinaryBoard.RowCount];
            for (int r = 0; r < BinaryBoard.RowCount; r++) board[r] = st.Board.RowBinary[r];
            var cand = new int[st.Trio.Length];
            Array.Copy(st.Trio, cand, st.Trio.Length);
            return new Snapshot
            {
                RngS0 = full.RngS0,
                RngS1 = full.RngS1,
                DynamicWeight = full.DynamicWeight,
                PreDynamicWeight = full.PreDynamicWeight,
                RefillIndex = full.RefillIndex,
                BcInWindow = full.BcInWindow,
                BcCooldown = full.BcCooldown,
                LastAlgo = full.LastAlgo,
                LastTierId = full.LastTierId,
                Board = board,
                Candidates = cand,
                Step = 0, // step 不参与发牌,仅占位
                Score = st.Score,
                LastTrioAlgo = st.LastTrioAlgo,
            };
        }

        /// <summary>等价 GameSessionHelper.Rehydrate:RNG 游标复位 + Init + ImportFullState。</summary>
        private static GameState Deserialize(in Snapshot snap)
        {
            var cfg = GenCoreDeterminismHarness.DefaultWeightConfig();
            var rng = new XorShift128PlusRng(snap.RngS0, snap.RngS1);
            var dyn = new DynamicWeightDiff(rng);
            dyn.ForceAlgorithm = null;
            dyn.Init(cfg);
            var full = new DynamicWeightDiff.FullState(
                snap.RngS0, snap.RngS1,
                snap.DynamicWeight, snap.PreDynamicWeight, snap.RefillIndex,
                snap.BcInWindow, snap.BcCooldown, snap.LastAlgo, snap.LastTierId);
            dyn.ImportFullState(full);

            var board = new BinaryBoard();
            for (int r = 0; r < BinaryBoard.RowCount; r++) board.RowBinary[r] = snap.Board[r];
            var trio = new int[snap.Candidates.Length];
            Array.Copy(snap.Candidates, trio, snap.Candidates.Length);

            return new GameState
            {
                Gen = dyn,
                Board = board,
                Trio = trio,
                LastTrioAlgo = snap.LastTrioAlgo,
                Score = snap.Score,
                Combo = 0, // combo 不持久(不影响发牌);trace 的 combo 列从续局点起按段内重算。
            };
        }

        /// <summary>
        /// 单步落子(等价 GameSessionHelper.Place + harness 玩家策略),把本步 trace 行写入 sb。
        /// 返回 false 表示卡死(清盘后仍无可放),终止。
        /// 注:combo 不参与发牌、也不进发牌器 trace 列,故 combo 不持久不影响逐位接续断言。
        /// </summary>
        private static bool StepOnce(ref GameState st, StringBuilder sb, int step)
        {
            bool forcedClear = false;
            if (!AnySlotPlaceable(st.Trio, st.Board))
            {
                for (int r = 0; r < BinaryBoard.RowCount; r++) st.Board.RowBinary[r] = 0;
                forcedClear = true;
            }

            int slot = -1; Vec2Int pos = default;
            for (int i = 0; i < 3; i++)
            {
                if (st.Trio[i] <= 0) continue;
                var poss = st.Board.GetCanPutPoss(st.Trio[i]);
                if (poss.Count > 0) { slot = i; pos = poss[0]; break; }
            }
            if (slot < 0)
            {
                sb.Append("step=").Append(step).Append(" EVENT=stuck_after_clear\n");
                return false;
            }

            int placedCells = BlockShapeMap.GetCellCount(st.Trio[slot]);
            st.Board.PutBlock(st.Trio[slot], pos);
            st.Trio[slot] = -1;

            var clear = st.Board.CanClearRowCols(true);
            int rows = clear.Rows.Count;
            int cols = clear.Cols.Count;
            int lines = rows + cols;
            int clearedCells = rows * BinaryBoard.ColCount + cols * BinaryBoard.RowCount - rows * cols;
            int placeScore = BlockScoring.PlacementScore(placedCells);
            int clearScore = lines > 0 ? BlockScoring.ClearScore(clearedCells, lines) : 0;
            st.Score += placeScore + clearScore;
            st.Combo = lines > 0 ? st.Combo + 1 : 0;

            st.Gen.AddWeight(st.LastTrioAlgo);

            bool refilled = false;
            if (AllConsumed(st.Trio))
            {
                var offer = st.Gen.OfferTrio(st.Board, st.Score);
                for (int i = 0; i < 3; i++) st.Trio[i] = offer.Ids[i];
                st.LastTrioAlgo = offer.Algo;
                refilled = true;
            }

            AppendStepLine(sb, step, slot, pos, lines, clearedCells, forcedClear, refilled,
                st.Trio, st.Score, st.Gen, st.Board);
            return true;
        }

        // trace 行:聚焦发牌相关列(trio/algo/tier/score/boardHash/发牌器标量),不含 combo
        // (combo 不持久,且与发牌无关;若纳入会引入续局点 combo 复位的伪差异)。
        private static void AppendStepLine(
            StringBuilder sb, int step, int slot, Vec2Int pos, int lines, int clearedCells,
            bool forcedClear, bool refilled, int[] trio, int score,
            DynamicWeightDiff dyn, BinaryBoard board)
        {
            sb.Append("step=").Append(step);
            sb.Append(" slot=").Append(slot);
            sb.Append(" pos=").Append(pos.X).Append(',').Append(pos.Y);
            sb.Append(" lines=").Append(lines);
            sb.Append(" clearedCells=").Append(clearedCells);
            sb.Append(" forcedClear=").Append(forcedClear ? 1 : 0);
            sb.Append(" refilled=").Append(refilled ? 1 : 0);
            sb.Append(" trio=");
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) sb.Append(';');
                if (trio[i] <= 0) sb.Append("null"); else sb.Append(trio[i]);
            }
            sb.Append(" algo=").Append((int)dyn.LastAlgo.GetValueOrDefault(AlgorithmKind.RandomNoDie));
            sb.Append(" tier=").Append(dyn.LastTierId.HasValue ? dyn.LastTierId.Value.ToString(CultureInfo.InvariantCulture) : "-");
            sb.Append(" score=").Append(score);
            sb.Append(" boardHash=").Append(BoardHash(board));
            sb.Append(" dynamicWeight=").Append(dyn.DynamicWeight);
            var ex = dyn.ExportFullState();
            sb.Append(" preDynamicWeight=").Append(ex.PreDynamicWeight);
            sb.Append(" refillIndex=").Append(ex.RefillIndex);
            sb.Append(" bcInWindow=").Append(ex.BcInWindow ? 1 : 0);
            sb.Append(" bcCooldown=").Append(ex.BcCooldown);
            sb.Append(" rngS0=").Append(ex.RngS0.ToString(CultureInfo.InvariantCulture));
            sb.Append(" rngS1=").Append(ex.RngS1.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
        }

        private static bool AnySlotPlaceable(int[] trio, BinaryBoard board)
        {
            for (int i = 0; i < 3; i++)
                if (trio[i] > 0 && board.CanPut(trio[i])) return true;
            return false;
        }

        private static bool AllConsumed(int[] trio)
        {
            for (int i = 0; i < 3; i++) if (trio[i] > 0) return false;
            return true;
        }

        private static string BoardHash(BinaryBoard board)
        {
            var sb = new StringBuilder(16);
            for (int r = 0; r < BinaryBoard.RowCount; r++)
                sb.Append(board.RowBinary[r].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static int FirstDivergenceLine(string a, string b)
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            int n = Math.Min(la.Length, lb.Length);
            for (int i = 0; i < n; i++)
                if (!string.Equals(la[i], lb[i], StringComparison.Ordinal)) return i + 1;
            return n + 1;
        }

        private static void DumpAround(string a, string b, int line)
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            int idx = line - 1;
            Console.WriteLine($"  oneShot[{line}]: {(idx < la.Length ? la[idx] : "<EOF>")}");
            Console.WriteLine($"  resumed[{line}]: {(idx < lb.Length ? lb[idx] : "<EOF>")}");
        }
    }
}
