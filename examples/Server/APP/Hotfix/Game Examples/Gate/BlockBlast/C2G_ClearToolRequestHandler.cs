using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using GameLogic.BlockBlast.Core;

namespace Fantasy;

/// <summary>
/// 消除道具请求(Outer RPC,运行在 Gate Scene,设计 49 §3.1 脱困道具)。
/// 清目标格所在整行整列,让卡死棋盘重新可落。请求只携带输入(目标格 + 幂等基准步号),
/// 清哪些格 / 扣多少体力全由服务端权威算 —— 不接受客户端上报体力值 / 被清格 / 扣费金额。
///
/// 与落子(C2G_Place)共用 Step 幂等三分支(消除道具与落子同一步号轴,都推进 Step):
///   baseStep == 权威 step → 执行:校验体力 → 扣体力 → 清行列 → 推进 Step → 存盘;
///   baseStep &lt;  权威 step → 幂等命中(已执行步重发),回当前权威态、不重复清、不重复扣体力;
///   baseStep &gt;  权威 step → 客户端超前,拒绝执行、回当前权威态供重同步。
///
/// 并发重发守卫(GameSession.ClearToolInFlight):消除道具把扣体力(await)放在 Step 推进之前,await 窗口内 Step 仍是旧值;
/// 弱网重发使两条同 baseStep 请求并发进 == 分支,若无守卫会双扣体力 + Step 自增 2(落子 Place 因 Step++ 同步先于 await 而天然免疫)。
/// 故 == 分支进入 await 之前**同步**置 ClearToolInFlight、finally 清位:置位期间后到的同动作请求同步短路回 IdempotentReplay 当前态,
/// 不进扣费/清盘路径。扣费成功后 Step 已推进,后续重发命中 baseStep &lt; Step 幂等;扣费失败清位后 Step 未动,重发仍走 == 重试。
///
/// 体力派生落账(范式同 C2G_DeliverOrderRequestHandler + MergeOrderServiceHelper.TryDeliver):
///   扣费 = MergeOrderConfigServer.ClearToolCost 常量(无返还),reason 服务端固定 "clear_tool:...";
///   走 PlayerPropertyServiceHelper.ChangeProperty(serverAuthoritative=true)= 上界 cap + 原子写 + ledger + 推送。
///   体力不足(ChangeProperty 返 NotEnough)→ 拒绝、不清棋盘、回带当前体力供客户端回滚乐观清。
///
/// 消除道具作为 board-mutating 动作推进 Step,但**不**消耗候选、**不**调 AddWeight/OfferTrio、**不**续发新批,
/// 故发牌调度态原样回带(客户端 deal 须同口径推进 Step、盘面用回带值覆盖、发牌器态不动)。
/// 清一行一列不清空全盘、不触发全清判定、不给全清奖(设计 49 §四)。
/// 所有结果以 ResultCode 回包,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_ClearToolRequestHandler : MessageRPC<C2G_ClearToolRequest, G2C_ClearToolResponse>
{
    protected override async FTask Run(Session session, C2G_ClearToolRequest request,
        G2C_ClearToolResponse response, Action reply)
    {
        var game = ResolveGame(session, request.GameId, out var resultCode);
        if (game == null)
        {
            response.ResultCode = resultCode;
            // 无对局 / 未登录:回带空态(Step/Score 默认 0,Board/GeneratorState 留空,NewEnergy 0)。
            await FTask.CompletedTask;
            return;
        }

        if (request.BaseStep < game.Step)
        {
            // 幂等命中:已执行步的重发,回当前权威态、不重复清、不重复扣体力。
            response.ResultCode = ClearToolResultCode.IdempotentReplay;
        }
        else if (request.BaseStep > game.Step)
        {
            // 客户端超前:拒绝执行,回当前权威态供重同步。
            response.ResultCode = ClearToolResultCode.StepAhead;
        }
        else if (game.ClearToolInFlight)
        {
            // 同一 ClearTool 动作已在途(前一条同 baseStep 请求正卡在扣体力 await 上,Step 尚未推进):
            // 弱网重发的第二条在此**同步**短路,回 IdempotentReplay 当前态,不进入扣费/清盘路径 —— 防双扣 + 双 Step。
            // 扣费成功后 Step 已推进,后续重发命中 baseStep < Step 幂等(见上一分支);扣费失败清位后 Step 未动,
            // 重发仍走 == 分支重试(与单条 NotEnough 语义一致,不会因守卫误吞一次合法重试)。
            response.ResultCode = ClearToolResultCode.IdempotentReplay;
        }
        else
        {
            // baseStep == 权威 step 且无同动作在途:同步置在途守卫,再进入扣体力 await。
            // 「查 ClearToolInFlight → 置位」在进入 await 前同步完成,同一 Scene 内 handler 仅在 await 点交错,故此区段原子。
            game.ClearToolInFlight = true;
            try
            {
                await ExecuteClearTool(session, game, request, response);
            }
            finally
            {
                game.ClearToolInFlight = false;
            }
        }

        // 不论分支,统一回带最新(或当前)权威态。消除道具不计分、不推进发牌,回带的 Score/GeneratorState 是当前值。
        response.Step = game.Step;
        response.Score = game.Score;
        GameSessionHelper.FillBoard(game, response.Board);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);

        await FTask.CompletedTask;
    }

    /// <summary>
    /// baseStep == 权威 step 分支:越界校验 → 服务端权威扣体力(体力不足即拒)→ 清行列 → 推进 Step → 存盘。
    /// 执行顺序保证「扣费失败绝不清盘、清盘成功必已扣费」:先扣体力(原子裁决含足额检查),扣成功才动棋盘。
    /// NewEnergy 语义:Cleared=扣后余额 / NotEnoughEnergy=当前余额;OutOfRange / ServiceUnavailable 不改体力,留 0。
    /// </summary>
    private static async FTask ExecuteClearTool(Session session, GameSession game,
        C2G_ClearToolRequest request, G2C_ClearToolResponse response)
    {
        // 越界校验先行:目标格必须在 8×8 界内(不推进 Step、不扣体力)。
        if (request.PosX < 0 || request.PosX >= BinaryBoard.ColCount ||
            request.PosY < 0 || request.PosY >= BinaryBoard.RowCount)
        {
            response.ResultCode = ClearToolResultCode.OutOfRange;
            return;
        }

        var accountId = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(accountId))
        {
            // ResolveGame 已保证有 GateAccountFlagComponent,理论不达此;防御性回未登录。
            response.ResultCode = ClearToolResultCode.NotLoggedIn;
            return;
        }

        // 服务端权威扣体力:delta = -ClearToolCost,serverAuthoritative=true(跳过客户端 RPC 路径单笔上限 + 频率闸,
        // 仍走足额检查 + 原子写 + ledger)。金额与 reason 均服务端固定,不接受客户端上报。
        long cost = MergeOrderConfigServer.ClearToolCost;
        var reason = $"clear_tool:g{game.GameId}:s{game.Step}:x{request.PosX}y{request.PosY}";
        var (energyResult, energyAfter) = await PlayerPropertyServiceHelper.ChangeProperty(
            session.Scene, accountId, PropertyType.Energy, -cost, reason, serverAuthoritative: true);

        if (energyResult == PropertyChangeResultCode.NotEnough)
        {
            // 体力不足:不清棋盘、不推进 Step,回带当前体力供客户端回滚乐观清。
            response.ResultCode = ClearToolResultCode.NotEnoughEnergy;
            response.NewEnergy = energyAfter; // ChangeProperty 在 NotEnough 时返当前实际余额
            return;
        }

        if (energyResult != PropertyChangeResultCode.Success)
        {
            // ServiceUnavailable / UnknownType / InvalidRequest / OverLimit(delta<0 不可能 OverLimit):
            // 服务不可用,不清棋盘、不推进 Step。
            Log.Warning($"[BlockBlast] ClearTool 扣体力失败 account={accountId} gameId={game.GameId} " +
                        $"cost={cost} result={energyResult} reason='{reason}'");
            response.ResultCode = ClearToolResultCode.ServiceUnavailable;
            return;
        }

        // 扣费成功:推送体力增量(与 DeliverOrder 同范式,让在线会话体力 HUD 对齐)。
        PlayerPropertyServiceHelper.SendDeltaPushTo(session.Scene, accountId, PropertyType.Energy, energyAfter, reason);

        // 清目标行列(权威棋盘)+ 推进 Step(不消耗候选、不推进发牌、不续发)。
        int clearedCells = GameSessionHelper.ClearTool(game, request.PosX, request.PosY);
        response.ResultCode = ClearToolResultCode.Cleared;
        response.ClearedCells = clearedCells;
        response.NewEnergy = energyAfter;

        // 清行列成功后存盘:盘面/步号落 Doc,供续局 / 快照恢复(与落子成功后存盘同口径)。
        var persistService = session.Scene.GetComponent<GameSessionServiceComponent>();
        await GameSessionPersistHelper.Save(persistService, GameSessionHelper.BuildDoc(game));

        Log.Info($"[BlockBlast] ClearTool account={accountId} gameId={game.GameId} step={game.Step} " +
                 $"pos=({request.PosX},{request.PosY}) cleared={clearedCells} cost={cost} newEnergy={energyAfter}");
    }

    /// <summary>从会话取登录时绑定的账号名(= Account.Name = accountId,ChangeProperty / ledger 同键)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        return account?.Name;
    }

    /// <summary>从会话取当前权威对局,并校验 gameId 一致。返回 null 时 out 给出对应结果码。</summary>
    private static GameSession? ResolveGame(Session session, long gameId, out ClearToolResultCode resultCode)
    {
        var accountFlag = session.GetComponent<GateAccountFlagComponent>();
        if (accountFlag == null)
        {
            resultCode = ClearToolResultCode.NotLoggedIn;
            return null;
        }

        var gameFlag = session.GetComponent<GameSessionFlagComponent>();
        GameSession? game = gameFlag?.GameSession;
        if (game == null || game.GameId != gameId)
        {
            resultCode = ClearToolResultCode.GameNotFound;
            return null;
        }

        resultCode = ClearToolResultCode.Cleared;
        return game;
    }
}
