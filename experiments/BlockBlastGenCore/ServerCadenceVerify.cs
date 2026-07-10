using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameLogic;
using GameLogic.Algorithms;
using GameLogic.Core;

namespace BlockBlastGenCore
{
    /// <summary>
    /// 服务端权威发牌环路·权威性回归自检(M2a 服务端段)。
    ///
    /// 独立复现真实服务端 Handler 的发牌节律 —— 与 examples/Server/.../GameSessionHelper 同一组
    /// 生成核心调用与同一口径(Init: new DynamicWeightDiff(rng) + Init/Reset/BeginGame + 首批 OfferTrio;
    /// 落子循环: harness 玩家策略落子→消耗该候选→整批消耗后 AddWeight(本批算法)+OfferTrio 续批),
    /// 落出与 <see cref="GenCoreDeterminismHarness.RunScriptedGame"/> 同格式的 trace。
    ///
    /// 断言:本驱动跑 seed=1337 的 trace 必须与 <see cref="GenCoreDeterminismHarness.RunSeed"/>(1337)
    /// (= 客户端 Mono 已逐位对齐的 golden gen-core trace)逐字符一致 —— 证明服务端权威发牌
    /// 与既有 golden 同序。
    /// </summary>
    internal static class ServerCadenceVerify
    {
        public static int Run(int seed)
        {
            string golden = GenCoreDeterminismHarness.RunSeed(seed);
            string server = RunServerCadence(seed, GenCoreDeterminismHarness.DefaultSteps);

            bool identical = string.Equals(golden, server, StringComparison.Ordinal);
            if (identical)
            {
                int lines = golden.Split('\n').Length;
                Console.WriteLine($"[server-cadence seed={seed}] 与 golden 逐字符一致 ✓  行数={lines}");
                return 0;
            }
            int div = FirstDivergenceLine(golden, server);
            Console.WriteLine($"[server-cadence seed={seed}] 与 golden 不一致 ✗  首个发散行={div}");
            DumpAround(golden, server, div);
            return 1;
        }

        /// <summary>
        /// 复现服务端 Handler 的发牌节律(等价 GameSessionHelper.Init + 多次 Place)。
        /// 玩家策略沿用 harness 口径(逐槽首个可放、落点取 GetCanPutPoss 固定序首、卡死清盘)。
        /// </summary>
        private static string RunServerCadence(int seed, int steps)
        {
            var cfg = GenCoreDeterminismHarness.DefaultWeightConfig();

            // === 等价 GameSessionHelper.Init ===
            IRandomSource rng = new XorShift128PlusRng(seed);
            var dyn = new DynamicWeightDiff(rng); // persistence 省略 = null(逐局态不落存储)
            dyn.ForceAlgorithm = null;
            dyn.Init(cfg);
            dyn.Reset();
            dyn.BeginGame();
            dyn.LastAlgo = null;
            dyn.LastTierId = null;

            var board = new BinaryBoard();
            int score = 0;
            int combo = 0;
            var trio = new int[] { -1, -1, -1 };
            AlgorithmKind lastTrioAlgo = AlgorithmKind.RandomNoDie;

            var sb = new StringBuilder(64 * 1024);
            AppendHeader(sb, seed, steps, cfg);

            // 首批:OfferTrio(空盘, score 0)。
            {
                var offer = dyn.OfferTrio(board, score);
                for (int i = 0; i < 3; i++) trio[i] = offer.Ids[i];
                lastTrioAlgo = offer.Algo;
            }

            for (int step = 0; step < steps; step++)
            {
                bool forcedClear = false;
                if (!AnySlotPlaceable(trio, board))
                {
                    for (int r = 0; r < BinaryBoard.RowCount; r++) board.RowBinary[r] = 0;
                    forcedClear = true;
                }

                int slot = -1; Vec2Int pos = default;
                for (int i = 0; i < 3; i++)
                {
                    if (trio[i] <= 0) continue;
                    var poss = board.GetCanPutPoss(trio[i]);
                    if (poss.Count > 0) { slot = i; pos = poss[0]; break; }
                }

                if (slot < 0)
                {
                    sb.Append("step=").Append(step).Append(" EVENT=stuck_after_clear\n");
                    break;
                }

                // === 等价 GameSessionHelper.Place ===
                int placedCells = BlockShapeMap.GetCellCount(trio[slot]);
                board.PutBlock(trio[slot], pos);
                trio[slot] = -1; // 消耗该候选

                var clear = board.CanClearRowCols(true);
                int rows = clear.Rows.Count;
                int cols = clear.Cols.Count;
                int lines = rows + cols;
                int clearedCells = rows * BinaryBoard.ColCount + cols * BinaryBoard.RowCount - rows * cols;
                int placeScore = BlockScoring.PlacementScore(placedCells);
                int clearScore = lines > 0 ? BlockScoring.ClearScore(clearedCells, lines) : 0;
                score += placeScore + clearScore;
                combo = lines > 0 ? combo + 1 : 0;

                // 每次落子都做权重反馈(与 golden / GameSessionHelper 同口径)。
                dyn.AddWeight(lastTrioAlgo);

                bool refilled = false;
                if (AllConsumed(trio))
                {
                    var offer = dyn.OfferTrio(board, score);
                    for (int i = 0; i < 3; i++) trio[i] = offer.Ids[i];
                    lastTrioAlgo = offer.Algo;
                    refilled = true;
                }

                AppendStepLine(sb, step, slot, pos, lines, clearedCells, forcedClear, refilled,
                    trio, score, combo, dyn, board);
            }

            return sb.ToString();
        }

        // ─── trace 格式与 GenCoreDeterminismHarness 完全对齐(逐位 diff 前提) ───

        private static void AppendHeader(StringBuilder sb, int seed, int steps, IList<WeightConfigEntry> cfg)
        {
            sb.Append("# block_blast gen-core determinism trace v1\n");
            sb.Append("# runtime-neutral plain text. generation-core only (no BlockGameState, no colors).\n");
            sb.Append("rng=XorShift128Plus\n");
            sb.Append("seed=").Append(seed).Append('\n');
            sb.Append("steps=").Append(steps).Append('\n');
            sb.Append("activation_score=").Append(GameConfigBB.ActivationScore).Append('\n');
            sb.Append("board_clear_score_threshold=15000\n");
            sb.Append("early_game_block_score_threshold=").Append(BlockShapeMap.EarlyGameBlockScoreThreshold).Append('\n');
            sb.Append("player_strategy=first_placeable_slot;first_pos_in_GetCanPutPoss_order\n");
            sb.Append("stuck_recovery=clear_whole_board\n");
            sb.Append("first_trio_source=OfferTrio_on_empty_board_score0\n");
            sb.Append("weightcfg_count=").Append(cfg.Count).Append('\n');
            foreach (var t in cfg)
            {
                sb.Append("weightcfg id=").Append(t.Id)
                  .Append(" odds=").Append(t.FillBlankOdds).Append(',').Append(t.RandomOdds).Append(',')
                  .Append(t.EntropyOdds).Append(',').Append(t.EasyOdds).Append(',').Append(t.HardOdds).Append(',')
                  .Append(t.IntuitionOdds).Append(',').Append(t.Clearboard).Append(',').Append(t.Allunite)
                  .Append(" hs=").Append(t.HighScoreMin).Append(',').Append(t.HighScoreMax)
                  .Append(" factor=").Append(t.FactorLow).Append(',').Append(t.FactorHigh)
                  .Append('\n');
            }
            sb.Append("# --- steps ---\n");
        }

        private static void AppendStepLine(
            StringBuilder sb, int step, int slot, Vec2Int pos, int lines, int clearedCells,
            bool forcedClear, bool refilled, int[] trio, int score, int combo,
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
            sb.Append(" combo=").Append(combo);
            sb.Append(" boardHash=").Append(BoardHash(board));
            sb.Append(" dynamicWeight=").Append(dyn.DynamicWeight);
            sb.Append(" preDynamicWeight=").Append(dyn.InternalPreDynamicWeight);
            sb.Append(" refillIndex=").Append(dyn.InternalRefillIndex);
            var bc = dyn.GetBoardClearState();
            sb.Append(" bcInWindow=").Append(bc.inWindow ? 1 : 0);
            sb.Append(" bcCooldown=").Append(bc.cooldown);
            sb.Append(" lastAlgo=").Append(dyn.LastAlgo.HasValue ? ((int)dyn.LastAlgo.Value).ToString(CultureInfo.InvariantCulture) : "-");
            sb.Append(" lastTier=").Append(dyn.LastTierId.HasValue ? dyn.LastTierId.Value.ToString(CultureInfo.InvariantCulture) : "-");
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
            Console.WriteLine($"  golden[{line}]: {(idx < la.Length ? la[idx] : "<EOF>")}");
            Console.WriteLine($"  server[{line}]: {(idx < lb.Length ? lb[idx] : "<EOF>")}");
        }
    }
}
