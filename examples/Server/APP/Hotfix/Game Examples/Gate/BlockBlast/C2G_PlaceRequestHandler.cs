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
                await GameSessionPersistHelper.Save(persistService, GameSessionHelper.BuildDoc(game));
            }
        }

        // 不论分支,统一回带最新(或当前)权威态。
        response.Step = game.Step;
        response.Score = game.Score;
        GameSessionHelper.FillBoard(game, response.Board);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);

        await FTask.CompletedTask;
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
