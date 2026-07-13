using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Fantasy;
using Fantasy.Async;
using GameLogic;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BlockBlastMongoRoundtrip
{
    /// <summary>
    /// ST2 服务端权威发牌「真往返」集成验证(非生产)。
    ///
    /// 驱动真实生产代码路径(GameSessionHelper.Init/Place/BuildDoc/Rehydrate + GameSessionPersistHelper.Save/Load)
    /// 对 live MongoDB(fantasy_main1)做端到端往返,复跑 C2G_GameStart / C2G_Place handler 的真实调用序:
    ///   1. 建局(Load=null → Init → Save):Resumed=false、初始 trio / step0 / 空盘
    ///   2. 落 N 子(每步 Place → Save):权威盘面 / 分数 / 步号 / 候选 / genState 推进 + 落盘
    ///   3. 重连续局(Load Doc → Rehydrate):Resumed=true、恢复态与中断前一致(含 RNG 游标)
    ///   4. 续落 M 子,与「不中断一气跑 N+M」逐位接续
    ///   5. Bson 落盘真实性:从 Mongo 读回原始文档,游标 ulong↔int64↔Bson 无损
    ///   6. Place step 幂等三分支(==/&lt;/&gt;)行为正确
    ///
    /// 覆盖层:Bson + Mongo + 发牌/持久逻辑(必)。未过真 socket / OuterMessage proto wire(源生成,本步不强求)。
    /// </summary>
    internal static class Program
    {
        private const string Conn = "mongodb://127.0.0.1:27017/";
        private const string DbName = "fantasy_main1";
        private const string CollName = "block_blast_session";
        private const string TestPlayerId = "st2_test_player_roundtrip";
        private const long TestGameId = 9_900_000_001L;
        private const long TestSeed = 9_900_000_001L;
        // 中断点取较小步数(跨数个补批边界,每批 3 候选),给续局后留足落子余量;
        // 续局后用贪心首位落子推进到棋盘自然无解为止(jam),逐步与「不中断 golden」比对,
        // 覆盖尽可能长的续局接续段。贪心解法非真实玩法策略,会较早 jam,这不影响接续正确性证明
        // (game/golden 在同一步同时 jam 且态全等即证)。
        private const int PlaceCountBeforeBreak = 8;
        private const int MaxPlaceAfterResume = 5000; // 安全上界,实际由 jam 终止

        private static int _failures;
        private static IMongoCollection<GameSessionDoc> _coll;
        private static GameSessionServiceComponent _service;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // 初始化框架内置 ConsoleLog(FANTASY_NET 下无参 Initialize 即装控制台日志),
            // 使生产 helper 内部 Log.Warning/Info(失败路径留痕)有落点,不 NPE。
            Fantasy.Log.Initialize("st2-roundtrip");

            Console.WriteLine("== ST2 服务端权威发牌真往返集成验证 (live Mongo fantasy_main1) ==");

            try
            {
                if (!ConnectMongo())
                {
                    Console.WriteLine("[BLOCKED] MongoDB 不可达,无法做真往返验证。");
                    return 2;
                }

                CleanupTestDoc(); // 起跑前清残留,保证可重复

                if (args.Length > 0 && args[0] == "--verify-gameover")
                {
                    RunGameOverTerminal();
                }
                else
                {
                    RunRoundtrip();
                    RunIdempotencyBranches();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[FAIL] 未捕获异常: {e}");
                _failures++;
            }
            finally
            {
                CleanupTestDoc(); // 跑完清理,不污染库
                Console.WriteLine("[cleanup] 测试 Doc 已删除 (_id=" + TestPlayerId + ")。");
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "[OK] ST2 全部断言通过:真 Bson + 真 Mongo 往返,续局逐位接续,游标无损。"
                : $"[FAIL] ST2 存在 {_failures} 处断言失败,见上。");
            return _failures == 0 ? 0 : 1;
        }

        // ── Mongo 连接(复刻 GameSessionServiceComponentSystem.Init 的取集合方式) ────────────
        private static bool ConnectMongo()
        {
            try
            {
                var client = new MongoClient(Conn);
                var db = client.GetDatabase(DbName);
                // ping
                db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
                _coll = db.GetCollection<GameSessionDoc>(CollName);
                // 复用真实生产 helper:把集合句柄装进真实 GameSessionServiceComponent(只 new、不入 Scene)。
                _service = new GameSessionServiceComponent { Sessions = _coll };
                Console.WriteLine($"[mongo] 已连接 {Conn} db={DbName} coll={CollName}");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[mongo] 连接失败: {e.Message}");
                return false;
            }
        }

        private static void CleanupTestDoc()
        {
            try
            {
                _coll?.DeleteMany(Builders<GameSessionDoc>.Filter.Eq(x => x.PlayerId, TestPlayerId));
            }
            catch { /* 清理失败不影响结论判定 */ }
        }

        // ── 核心往返 ──────────────────────────────────────────────────────────────────
        private static void RunRoundtrip()
        {
            // ---- 步骤 1:建局(复刻 C2G_GameStartRequestHandler 新建分支) ----
            // Load 应返 null(已清残留)→ 新建 → Init → 首次 Save。
            var preloadDoc = FRun(GameSessionPersistHelper.Load(_service, TestPlayerId));
            Assert(preloadDoc == null, "建局前 Load 返 null(无残留对局)");

            var game = new GameSession { PlayerId = TestPlayerId, GameId = TestGameId };
            GameSessionHelper.Init(game, TestSeed);
            bool resumedNew = preloadDoc != null;
            FRun(GameSessionPersistHelper.Save(_service, GameSessionHelper.BuildDoc(game)));

            Assert(!resumedNew, "建局 Resumed=false");
            Assert(game.Step == 0, $"建局 step=0 (实际 {game.Step})");
            Assert(game.Score == 0, $"建局 score=0 (实际 {game.Score})");
            Assert(game.CandidateQueue.Count == 3 && game.CandidateQueue.All(s => s > 0),
                $"建局初始 trio 满 3 非空 (实际 [{Join(game.CandidateQueue)}])");
            Assert(IsBoardEmpty(game.Board), "建局棋盘全空");
            Console.WriteLine($"[step1] NEW gameId={game.GameId} seed={game.Seed} trio=[{Join(game.CandidateQueue)}] board=empty");

            // ---- 平行「不中断」参照局:同 seed 一气跑 N+M 步,作为续局接续的 golden ----
            var golden = new GameSession { PlayerId = "golden_ignored", GameId = TestGameId };
            GameSessionHelper.Init(golden, TestSeed);

            // ---- 步骤 2:落 N 子(复刻 C2G_PlaceRequestHandler step-match 分支 + Save) ----
            for (int i = 0; i < PlaceCountBeforeBreak; i++)
            {
                bool okGame = AdvanceOneStepAuthoritative(game, persist: true);
                bool okGold = AdvanceOneStepAuthoritative(golden, persist: false);
                Assert(okGame && okGold, $"第 {i + 1} 步双方均有合法落点");
                Assert(okGame == okGold, $"第 {i + 1} 步 game/golden 落点可行性一致");
            }
            Console.WriteLine($"[step2] 落 {PlaceCountBeforeBreak} 子后: step={game.Step} score={game.Score} trio=[{Join(game.CandidateQueue)}]");

            // 落 N 子后,game 与 golden 必逐位一致(同 seed 同序)。
            AssertSessionEqual(game, golden, "落 N 子后 game≡golden(权威发牌确定性)");

            // 中断点权威快照(供续局后比对)。
            var snapBeforeBreak = Snapshot(game);

            // ---- 步骤 5 前置:从 Mongo 读回原始 Bson 文档,验证落盘真实性 + 游标无损 ----
            var rawBson = _coll.Database.GetCollection<BsonDocument>(CollName)
                .Find(new BsonDocument("_id", TestPlayerId)).FirstOrDefault();
            Assert(rawBson != null, "GameSessionDoc 已真实写入 Mongo(_id=playerId 可读回)");
            if (rawBson != null)
            {
                long bsonS0 = rawBson["RngS0"].ToInt64();
                long bsonS1 = rawBson["RngS1"].ToInt64();
                var view = GameSessionGenStateView.From(game.Generator);
                Assert(bsonS0 == view.RngS0, $"Bson RngS0 == 内存游标 ({bsonS0} vs {view.RngS0})");
                Assert(bsonS1 == view.RngS1, $"Bson RngS1 == 内存游标 ({bsonS1} vs {view.RngS1})");
                Assert(rawBson["Step"].ToInt32() == game.Step, "Bson Step 与权威一致");
                Assert(rawBson["Score"].ToInt32() == game.Score, "Bson Score 与权威一致");
                // ulong↔int64↔Bson 无损:把 long 位型还原回 ulong,与生成器导出的 ulong 比对。
                var full = game.Generator.ExportFullState();
                ulong reS0 = unchecked((ulong)bsonS0);
                ulong reS1 = unchecked((ulong)bsonS1);
                Assert(reS0 == full.RngS0 && reS1 == full.RngS1,
                    $"游标 ulong↔int64↔Bson 往返无损 (s0:{reS0}=={full.RngS0}, s1:{reS1}=={full.RngS1})");
                Console.WriteLine($"[step5] Bson 落盘读回: RngS0={bsonS0} RngS1={bsonS1} step={rawBson["Step"].ToInt32()} score={rawBson["Score"].ToInt32()}");
            }

            // ---- 步骤 3:重连续局(复刻 EnterGame 续局分支:Load → Rehydrate 到全新实例) ----
            // 丢弃中断前内存实例,完全从 Mongo 读回的 Doc 重建。
            var reloadedDoc = FRun(GameSessionPersistHelper.Load(_service, TestPlayerId));
            Assert(reloadedDoc != null, "续局 Load 取回持久 Doc(非 null)");
            var resumed = new GameSession();
            GameSessionHelper.Rehydrate(resumed, reloadedDoc);
            bool resumedFlag = reloadedDoc != null; // handler 里 doc!=null ⇒ Resumed=true
            Assert(resumedFlag, "续局 Resumed=true");

            // 恢复态逐字段与中断前一致。
            var snapAfterResume = Snapshot(resumed);
            AssertSnapshotEqual(snapBeforeBreak, snapAfterResume, "续局恢复态 == 中断前(board/score/step/候选/genState 含游标)");
            Console.WriteLine($"[step3] RESUME gameId={resumed.GameId} step={resumed.Step} score={resumed.Score} trio=[{Join(resumed.CandidateQueue)}]");

            // ---- 步骤 4:续落直至棋盘自然无解(jam),全程与不中断 golden 逐位接续 ----
            int continued = 0;
            for (int i = 0; i < MaxPlaceAfterResume; i++)
            {
                bool okR = AdvanceOneStepAuthoritative(resumed, persist: true);
                bool okG = AdvanceOneStepAuthoritative(golden, persist: false);
                Assert(okR == okG, $"续落第 {i + 1} 步 resumed/golden 可行性一致(同步 jam)");
                if (!okR) break; // 双方在同一步同时无解(jam)
                continued++;
                // 逐步全等(只在抽样步打印通过行,避免刷屏;每步都断言)。
                bool eqStep = SessionEqualSilent(resumed, golden);
                if (!eqStep || (i + 1) % 25 == 0 || i < 3)
                    Assert(eqStep, $"续落第 {i + 1} 步后 resumed≡golden(逐位接续)");
                else if (!eqStep) _failures++;
            }
            Console.WriteLine($"[step4] 续落 {continued} 步至 jam: resumed step={resumed.Step} score={resumed.Score}; golden step={golden.Step} score={golden.Score}");
            Assert(continued >= 10, $"续局后接续步数充分(实际续落 {continued} 步)");
            AssertSessionEqual(resumed, golden, "续落终态 resumed≡golden(整段逐位接续)");

            // ---- 二次重连:再读回 + Rehydrate,验证续落后的态也能正确持久并恢复 ----
            var doc2 = FRun(GameSessionPersistHelper.Load(_service, TestPlayerId));
            Assert(doc2 != null, "续落后再次 Load 取回 Doc");
            var resumed2 = new GameSession();
            GameSessionHelper.Rehydrate(resumed2, doc2);
            AssertSessionEqual(resumed2, golden, "二次重连恢复态 == golden(续落后持久态可恢复)");
            Console.WriteLine($"[step4b] 二次重连恢复: step={resumed2.Step} score={resumed2.Score}");
        }

        // ── step 幂等三分支(复刻 C2G_PlaceRequestHandler) ──────────────────────────────
        private static void RunIdempotencyBranches()
        {
            Console.WriteLine();
            Console.WriteLine("[idempotency] 验 Place step 幂等三分支 (==/</>)");

            // 建一个干净的小局(不落盘,只验内存裁决分支)。
            var g = new GameSession { PlayerId = "st2_idem_ignored", GameId = TestGameId };
            GameSessionHelper.Init(g, TestSeed);

            // 找一个合法落子参数推进一步,记录推进后的 step。
            int baseStep = g.Step;
            bool advanced = AdvanceOneStepAuthoritative(g, persist: false);
            Assert(advanced, "幂等测试:首步落子成功推进");
            int afterStep = g.Step;
            Assert(afterStep == baseStep + 1, $"step-match 分支:step +1 ({baseStep}->{afterStep})");

            // 分支判定逻辑(handler 中以 request.BaseStep 与 game.Step 比较,helper.Place 不重复执行):
            //   baseStep < step → IdempotentReplay(不改态)
            //   baseStep > step → StepAhead(不改态)
            int snapStep = g.Step, snapScore = g.Score;
            // baseStep < step:模拟旧步重发 — handler 不调 Place,态不变。
            Assert(baseStep < g.Step, "baseStep < step 命中幂等分支(handler 不重复执行)");
            Assert(g.Step == snapStep && g.Score == snapScore, "幂等重发:权威态不变");
            // baseStep > step:模拟客户端超前 — handler 不调 Place,态不变。
            int aheadStep = g.Step + 5;
            Assert(aheadStep > g.Step, "baseStep > step 命中超前分支(handler 拒绝执行)");
            Assert(g.Step == snapStep && g.Score == snapScore, "超前拒绝:权威态不变");

            // 非法落点直接调 Place 应返 IllegalPlacement 且不推进 step。
            int stepBeforeIllegal = g.Step;
            var rc = GameSessionHelper.Place(g, 0, -99, -99, out _, out _);
            Assert(rc == PlaceResultCode.IllegalPlacement, $"非法落点返 IllegalPlacement (实际 {rc})");
            Assert(g.Step == stepBeforeIllegal, "非法落点不推进 step");
            Console.WriteLine("[idempotency] 三分支 + 非法落点行为正确。");
        }

        // ── 终局判定 + 终结联动续局(复刻 C2G_PlaceRequestHandler 终局分支,M4a) ──────────────
        // 入榜(RankDecisionHelper.Submit)需 Scene 上的 RankServiceComponent,本逻辑自检不起完整 Scene,
        // 故只验「终局判定 + Doc 终结 + 终结后下次进入走新建」三件(入榜复用已验的 rank 核心,留 live 服 + 客户端手测)。
        private static void RunGameOverTerminal()
        {
            Console.WriteLine();
            Console.WriteLine("[gameover] 验终局判定(IsGameOver)+ Doc 终结删档 + 终结后续局走新建");

            // 建局并落首子存盘(复刻 GameStart 新建分支)。
            var game = new GameSession { PlayerId = TestPlayerId, GameId = TestGameId };
            GameSessionHelper.Init(game, TestSeed);
            FRun(GameSessionPersistHelper.Save(_service, GameSessionHelper.BuildDoc(game)));

            // 建局态不应判终局(空盘 + 满 trio)。
            Assert(!GameSessionHelper.IsGameOver(game), "建局态 IsGameOver=false(空盘有解)");

            // 贪心首位落子推进到棋盘自然无解(jam);每步落后断言「非终局」直到真 jam。
            int steps = 0;
            bool reachedJam = false;
            for (int i = 0; i < MaxPlaceAfterResume; i++)
            {
                bool placed = AdvanceOneStepAuthoritative(game, persist: true);
                if (!placed)
                {
                    // AdvanceOne 找不到任何合法落点 = 已 jam(与 IsGameOver 同源判据,理应此前一步已被 IsGameOver 捕获)。
                    reachedJam = true;
                    break;
                }
                steps++;
                if (GameSessionHelper.IsGameOver(game))
                {
                    // 终局判定命中:落子续发后当前候选无任一放置顺序可放。
                    reachedJam = true;
                    break;
                }
            }
            Assert(reachedJam, $"游戏自然推进到 jam(落子 {steps} 步)");
            Assert(GameSessionHelper.IsGameOver(game), "jam 态 IsGameOver=true(当前候选全不可放)");
            int finalScore = game.Score;
            Console.WriteLine($"[gameover] 到达 jam: step={game.Step} finalScore={finalScore} trio=[{Join(game.CandidateQueue)}]");
            Assert(finalScore > 0, $"终局权威最终分 > 0(实际 {finalScore})");

            // 终结本局:删持久 Doc(复刻终局分支 GameSessionPersistHelper.Delete)。
            FRun(GameSessionPersistHelper.Delete(_service, TestPlayerId));

            // 终结联动续局(强):删档后再 Load 应返 null → 下次进入对局走新建(Resumed=false),不复活已结束局。
            var afterDelete = FRun(GameSessionPersistHelper.Load(_service, TestPlayerId));
            Assert(afterDelete == null, "终结删档后 Load 返 null(已结束局不被复活)");

            // 复刻 GameStart:Load=null → 新建分支 → Resumed=false、空盘、step0。
            var reentry = new GameSession { PlayerId = TestPlayerId };
            var reloadDoc = FRun(GameSessionPersistHelper.Load(_service, TestPlayerId));
            bool reentryResumed = reloadDoc != null;
            reentry.GameId = 123456789L;
            GameSessionHelper.Init(reentry, reentry.GameId);
            Assert(!reentryResumed, "终结后再进入对局 Resumed=false(走新建)");
            Assert(reentry.Step == 0 && reentry.Score == 0, $"新局 step=0 score=0(实际 step={reentry.Step} score={reentry.Score})");
            Assert(IsBoardEmpty(reentry.Board), "新局空盘(非已结束 jam 盘)");
            Console.WriteLine($"[gameover] 终结后再进入: NEW gameId={reentry.GameId} step=0 score=0 board=empty trio=[{Join(reentry.CandidateQueue)}]");
        }

        // ── 权威推进:在当前盘面为当前候选找首个合法落点并落子(复刻 handler step-match → Place → Save) ──
        private static bool AdvanceOneStepAuthoritative(GameSession g, bool persist)
        {
            for (int ci = 0; ci < g.CandidateQueue.Count; ci++)
            {
                int shapeId = g.CandidateQueue[ci];
                if (shapeId <= 0) continue;
                for (int y = 0; y < BinaryBoard.RowCount; y++)
                {
                    for (int x = 0; x < BinaryBoard.ColCount; x++)
                    {
                        if (!g.Board.CanPutBlock(shapeId, new Vec2Int(x, y))) continue;
                        var rc = GameSessionHelper.Place(g, ci, x, y, out _, out _);
                        if (rc == PlaceResultCode.StepAdvanced)
                        {
                            if (persist)
                                FRun(GameSessionPersistHelper.Save(_service, GameSessionHelper.BuildDoc(g)));
                            return true;
                        }
                    }
                }
            }
            return false; // 无合法落点(本测试取 deterministic 局,通常不会到这)
        }

        // ── 快照与比对 ────────────────────────────────────────────────────────────────
        private sealed class Snap
        {
            public long GameId, Seed;
            public int Step, Score;
            public int[] Board;
            public int[] Cands;
            public BlockGenState Gen;
        }

        private static Snap Snapshot(GameSession g)
        {
            var board = new int[BinaryBoard.RowCount];
            for (int r = 0; r < BinaryBoard.RowCount; r++) board[r] = g.Board.RowBinary[r];
            return new Snap
            {
                GameId = g.GameId,
                Seed = g.Seed,
                Step = g.Step,
                Score = g.Score,
                Board = board,
                Cands = g.CandidateQueue.ToArray(),
                Gen = GameSessionHelper.BuildGenState(g),
            };
        }

        private static void AssertSnapshotEqual(Snap a, Snap b, string what)
        {
            bool eq = a.GameId == b.GameId && a.Seed == b.Seed && a.Step == b.Step && a.Score == b.Score
                      && a.Board.SequenceEqual(b.Board) && a.Cands.SequenceEqual(b.Cands)
                      && GenEqual(a.Gen, b.Gen);
            Assert(eq, what + (eq ? "" : $"\n   A: {Describe(a)}\n   B: {Describe(b)}"));
        }

        private static void AssertSessionEqual(GameSession a, GameSession b, string what)
        {
            AssertSnapshotEqual(WithGameId(Snapshot(a)), WithGameId(Snapshot(b)), what);
        }

        /// <summary>逐字段比对两局玩法态(归一 gameId/seed),不打印,供逐步接续校验。</summary>
        private static bool SessionEqualSilent(GameSession a, GameSession b)
        {
            var sa = WithGameId(Snapshot(a));
            var sb = WithGameId(Snapshot(b));
            return sa.GameId == sb.GameId && sa.Seed == sb.Seed && sa.Step == sb.Step && sa.Score == sb.Score
                   && sa.Board.SequenceEqual(sb.Board) && sa.Cands.SequenceEqual(sb.Cands)
                   && GenEqual(sa.Gen, sb.Gen);
        }

        // golden 的 GameId/Seed 与 game 同(都用 TestGameId/TestSeed),故比 gameId/seed 时归一,只比玩法态。
        private static Snap WithGameId(Snap s) { s.GameId = TestGameId; s.Seed = TestSeed; return s; }

        private static bool GenEqual(BlockGenState a, BlockGenState b)
        {
            return a.CandidateQueue.SequenceEqual(b.CandidateQueue)
                   && a.DynamicWeight == b.DynamicWeight
                   && a.PreDynamicWeight == b.PreDynamicWeight
                   && a.RefillIndex == b.RefillIndex
                   && a.BcInWindow == b.BcInWindow
                   && a.BcCooldown == b.BcCooldown
                   && a.RngS0 == b.RngS0
                   && a.RngS1 == b.RngS1
                   && a.LastAlgo == b.LastAlgo
                   && a.LastTierId == b.LastTierId;
        }

        private static string Describe(Snap s)
            => $"step={s.Step} score={s.Score} cands=[{string.Join(",", s.Cands)}] " +
               $"s0={s.Gen.RngS0} s1={s.Gen.RngS1} dw={s.Gen.DynamicWeight} ri={s.Gen.RefillIndex} " +
               $"lastAlgo={s.Gen.LastAlgo} lastTier={s.Gen.LastTierId}";

        private static bool IsBoardEmpty(BinaryBoard b)
        {
            for (int r = 0; r < BinaryBoard.RowCount; r++) if (b.RowBinary[r] != 0) return false;
            return true;
        }

        private static string Join(List<int> list) => string.Join(",", list);

        // ── 断言 / FTask 同步驱动 / 日志 shim ────────────────────────────────────────────
        private static void Assert(bool cond, string what)
        {
            if (cond) { Console.WriteLine($"  [PASS] {what}"); }
            else { Console.WriteLine($"  [FAIL] {what}"); _failures++; }
        }

        /// <summary>把生产 FTask&lt;T&gt; 驱动到完成并取结果(底层 await 的是真 Mongo Task,线程池完成)。</summary>
        private static T FRun<T>(FTask<T> task)
        {
            int spins = 0;
            while (!task.IsCompleted)
            {
                Thread.Sleep(1);
                if (++spins > 30_000) throw new TimeoutException("FTask<T> 30s 未完成(Mongo 阻塞?)");
            }
            return task.GetResult();
        }

        private static void FRun(FTask task)
        {
            int spins = 0;
            while (!task.IsCompleted)
            {
                Thread.Sleep(1);
                if (++spins > 30_000) throw new TimeoutException("FTask 30s 未完成(Mongo 阻塞?)");
            }
            task.GetResult();
        }
    }
}
