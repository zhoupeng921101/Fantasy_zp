using System;
using System.IO;
using System.Text;
using GameLogic;

namespace BlockBlastGenCore
{
    /// <summary>
    /// 服务端权威发牌移植·跨运行时确定性测量实验入口(非生产)。
    ///
    /// 把客户端 BlockBlast 生成核心链接进 .NET 运行,两种 RNG 模式各跑两遍断言逐字符一致
    /// (.NET 内部确定性 + 端口忠实),并落两份服务端 trace:
    ///   A. System.Random —— 供与客户端 System.Random trace 比对、量化 c3
    ///   B. Portable(xorshift128+) —— 供与客户端 Portable trace 比对、量化 c1/c2
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 服务端权威发牌环路·权威性回归自检模式:复现真实 Handler 发牌节律,逐 seed 与 golden 逐位 diff。
            if (args.Length > 0 && args[0] == "--verify-server-cadence")
            {
                int rcv = 0;
                foreach (int s in GenCoreDeterminismHarness.Seeds)
                {
                    rcv |= ServerCadenceVerify.Run(s);
                }
                Console.WriteLine(rcv == 0
                    ? "[OK] 服务端权威发牌节律与 golden 逐位一致(全 seed)。"
                    : "[FAIL] 服务端权威发牌节律与 golden 存在发散,见上。");
                return rcv;
            }

            // 续局忠实性自检:建局→落 N 子→序列化全态→重建→续 M 子,与一气跑完 N+M 逐位 diff。
            // 全 seed × 多 (N,M) 切点(含「补批边界附近」「跨补批」),覆盖整批消耗/补牌点的续接。
            if (args.Length > 0 && args[0] == "--verify-resume")
            {
                int rcr = 0;
                var cuts = new (int n, int m)[] { (1, 50), (3, 50), (4, 50), (10, 60), (37, 80), (120, 120) };
                foreach (int s in GenCoreDeterminismHarness.Seeds)
                {
                    foreach (var (n, m) in cuts)
                    {
                        rcr |= ResumeContinuityVerify.Run(s, n, m);
                    }
                }
                Console.WriteLine(rcr == 0
                    ? "[OK] 续局重建后发牌与中断前逐位接续(全 seed × 全切点)。"
                    : "[FAIL] 续局重建后发牌与一气跑完存在发散,见上。");
                return rcr;
            }

            // 去单例化后调度器逐局 new,持久化由 harness 内部注入 InMemory;此处无需设全局 Persistence。
            string outDir = args.Length > 0
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "traces");
            Directory.CreateDirectory(outDir);

            int rc = 0;

            // Portable 多 seed(生产路径):种子表取自共享 harness 的单一事实源,每 seed 落带 seed 的 trace。
            foreach (int seed in GenCoreDeterminismHarness.Seeds)
            {
                rc |= RunPortableSeed(seed,
                    Path.Combine(outDir, $"gencore_trace_server_portable_seed{seed}.txt"));
            }

            // System.Random 单 seed(c3 留档):c3 已测完,保留一条供对照。
            rc |= RunMode(GenCoreDeterminismHarness.RngKind.SystemRandom, 1337,
                Path.Combine(outDir, "gencore_trace_server_systemrandom.txt"));

            Console.WriteLine(rc == 0
                ? "[OK] 所有 seed/模式均通过同 seed 两遍逐字符一致自检。"
                : "[FAIL] 存在两遍不一致项,见上。");
            return rc;
        }

        /// <summary>Portable 模式单 seed 两遍一致自检 + 落 trace。RunSeed 即 Portable + DefaultSteps。</summary>
        private static int RunPortableSeed(int seed, string path)
        {
            string a = GenCoreDeterminismHarness.RunSeed(seed);
            string b = GenCoreDeterminismHarness.RunSeed(seed);
            return Report($"Portable seed={seed}", a, b, path);
        }

        private static int RunMode(GenCoreDeterminismHarness.RngKind rng, int seed, string path)
        {
            string a = GenCoreDeterminismHarness.RunScriptedGame(seed, GenCoreDeterminismHarness.DefaultSteps, rng);
            string b = GenCoreDeterminismHarness.RunScriptedGame(seed, GenCoreDeterminismHarness.DefaultSteps, rng);
            return Report($"{rng} seed={seed}", a, b, path);
        }

        private static int Report(string label, string a, string b, string path)
        {
            bool identical = string.Equals(a, b, StringComparison.Ordinal);
            File.WriteAllText(path, a, new UTF8Encoding(false));
            int lines = a.Split('\n').Length;
            if (identical)
            {
                Console.WriteLine($"[{label}] 两遍逐字符一致 ✓  行数={lines}  → {path}");
                return 0;
            }
            int div = FirstDivergenceLine(a, b);
            Console.WriteLine($"[{label}] 两遍不一致 ✗  首个发散行={div}  → {path}");
            return 1;
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
    }
}
