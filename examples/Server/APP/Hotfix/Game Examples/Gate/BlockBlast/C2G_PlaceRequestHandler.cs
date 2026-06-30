using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 落子请求(Outer RPC,运行在 Gate Scene)。
/// 要害:请求只携带玩家输入(候选槽位 + 落点),绝不携带形状 shapeId —— 形状由服务端权威持有,
/// 客户端无从指定,关闭选块作弊面。
///
/// step 幂等三分支:
///   baseStep == 权威 step → 执行落子,推进;
///   baseStep <  权威 step → 幂等命中(已执行步重发),回当前权威态、不重复执行;
///   baseStep >  权威 step → 客户端超前,拒绝执行、回当前权威态供重同步。
/// 所有结果以 ResultCode 回包,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_PlaceRequestHandler : MessageRPC<C2G_PlaceRequest, G2C_PlaceResponse>
{
    protected override async FTask Run(Session session, C2G_PlaceRequest request,
        G2C_PlaceResponse response, Action reply)
    {
        response.NewCandidate = -1;
        bool gameOver = false;

        var game = ResolveGame(session, request.GameId, out var resultCode);
        if (game == null)
        {
            response.ResultCode = resultCode;
            // 无对局:回带空态(Step/Score 默认 0,Board/GeneratorState 留空)。
            await FTask.CompletedTask;
            return;
        }

        if (request.BaseStep < game.Step)
        {
            // 幂等命中:已执行步的重发,回当前权威态。
            response.ResultCode = PlaceResultCode.IdempotentReplay;
        }
        else if (request.BaseStep > game.Step)
        {
            // 客户端超前:拒绝执行,回当前权威态供重同步。
            response.ResultCode = PlaceResultCode.StepAhead;
        }
        else
        {
            // baseStep == 权威 step:执行落子裁决。
            response.ResultCode = GameSessionHelper.Place(
                game, request.CandidateIndex, request.PosX, request.PosY,
                out int eliminatedLines, out int newCandidate);
            response.EliminatedLines = eliminatedLines;
            response.NewCandidate = newCandidate;

            // 仅在权威态真推进(成功落子)时存盘:盘面/分数/步号/发牌器全态落 Doc,供续局恢复。
            // 存盘失败不回滚裁决(权威态已在内存推进),下次落子存盘补上(见 GameSessionPersistHelper.Save)。
            if (response.ResultCode == PlaceResultCode.StepAdvanced)
            {
                var persistService = session.Scene.GetComponent<GameSessionServiceComponent>();

                // 终局判定:落子续发后,当前候选无任一放置顺序可放(jam)→ 本局结束。
                if (GameSessionHelper.IsGameOver(game))
                {
                    gameOver = true;
                    await SettleGameOver(session, game, persistService, response);
                }
                else
                {
                    await GameSessionPersistHelper.Save(persistService, GameSessionHelper.BuildDoc(game));
                }
            }
        }

        // 不论分支,统一回带最新(或当前)权威态。终局分支的 Doc 已删、会话 flag 已清,但 game 内存实例尚未 Dispose
        // (本调用栈末尾才回收),此处仍可读出终局权威态回带。
        response.Step = game.Step;
        response.Score = game.Score;
        GameSessionHelper.FillBoard(game, response.Board);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);

        // 终局回带完成后再回收内存实例(避免 Dispose 后读到对象池复位态)。
        if (gameOver)
        {
            game.Dispose();
        }

        await FTask.CompletedTask;
    }

    /// <summary>
    /// 终局结算(服务端权威):用权威最终分入排行榜 → 删持久 Doc(终结本局,下次进入走新建) → 清会话 flag 指向。
    /// 内存实例的 Dispose 由调用方(Run)在回带终局权威态之后执行(避免 Dispose 后读对象池复位态)。
    /// 入榜走 in-process 直调 RankDecisionHelper.Submit(优选路径):身份用会话上 Account.Name(与客户端 C2G 上报同键,
    /// 保证服务端代提与客户端自报落同一行、myRank 一致),分用 game.Score(服务端权威),过 rank 自身入榜门槛/最佳分/反作弊。
    /// 提交到周榜(1)+总榜(2)两榜:一局无尽分同时计入周榜与总榜(与既有榜定义口径一致)。
    /// 回带 GameOver=true / FinalScore=权威分 / BestScore=总榜入榜后最佳(服务不可用则 0)。
    /// </summary>
    private static async FTask SettleGameOver(Session session, GameSession game,
        GameSessionServiceComponent? persistService, G2C_PlaceResponse response)
    {
        int finalScore = game.Score;
        long bestScore = 0L;

        // 入榜:服务端权威分直提 rank 核心(优选路径)。身份用 Account.Name(与客户端上报同键)。
        var accountName = GetSessionAccountName(session);
        var rankService = session.Scene.GetComponent<RankServiceComponent>();
        if (rankService != null && !string.IsNullOrEmpty(accountName))
        {
            // 周榜(1):入榜要求 100,低分自然被门槛挡(BelowEnterRequirement,无害)。
            await RankDecisionHelper.Submit(rankService, accountName, 1, finalScore);
            // 总榜(2):入榜要求 0,作为终局最佳分回带依据。
            var (_, totalBest) = await RankDecisionHelper.Submit(rankService, accountName, 2, finalScore);
            bestScore = totalBest;
        }
        else
        {
            Log.Warning($"[BlockBlast] GameOver 入榜跳过(rank 服务未就绪或会话无账号) playerId={game.PlayerId} finalScore={finalScore}");
        }

        // 终结本局:删持久 Doc(下次进入 Load 返 null → 新建 Resumed=false,不复活已结束局)。
        await GameSessionPersistHelper.Delete(persistService, game.PlayerId);

        // 清会话 flag 指向(开新局前会话不再指向已结束局)。内存实例 Dispose 由 Run 在回带后执行。
        var gameFlag = session.GetComponent<GameSessionFlagComponent>();
        gameFlag?.GameSession.Clear();

        response.GameOver = true;
        response.FinalScore = finalScore;
        response.BestScore = bestScore;

        Log.Info($"[BlockBlast] GameOver playerId={game.PlayerId} gameId={game.GameId} step={game.Step} " +
                 $"finalScore={finalScore} totalBest={bestScore} board=[{string.Join(",", game.CandidateQueue)}]");
    }

    /// <summary>从会话取登录时绑定的账号名(与 rank 上报/查榜同键)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        return account?.Name;
    }

    /// <summary>从会话取当前权威对局,并校验 gameId 一致。返回 null 时 out 给出对应结果码。</summary>
    private static GameSession? ResolveGame(Session session, long gameId, out PlaceResultCode resultCode)
    {
        var accountFlag = session.GetComponent<GateAccountFlagComponent>();
        if (accountFlag == null)
        {
            resultCode = PlaceResultCode.NotLoggedIn;
            return null;
        }

        var gameFlag = session.GetComponent<GameSessionFlagComponent>();
        GameSession? game = gameFlag?.GameSession;
        if (game == null || game.GameId != gameId)
        {
            resultCode = PlaceResultCode.GameNotFound;
            return null;
        }

        resultCode = PlaceResultCode.StepAdvanced;
        return game;
    }
}
