using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 快照请求(Outer RPC,运行在 Gate Scene)。重连 / 恢复用:回带当前对局完整权威态。
/// 身份从会话取;gameId 不匹配或无对局 → GameNotFound。
/// </summary>
public sealed class C2G_GameSnapshotRequestHandler : MessageRPC<C2G_GameSnapshotRequest, G2C_GameSnapshotResponse>
{
    protected override async FTask Run(Session session, C2G_GameSnapshotRequest request,
        G2C_GameSnapshotResponse response, Action reply)
    {
        var accountFlag = session.GetComponent<GateAccountFlagComponent>();
        if (accountFlag == null)
        {
            response.ResultCode = GameSnapshotResultCode.NotLoggedIn;
            await FTask.CompletedTask;
            return;
        }

        var gameFlag = session.GetComponent<GameSessionFlagComponent>();
        GameSession? game = gameFlag?.GameSession;
        if (game == null || game.GameId != request.GameId)
        {
            response.ResultCode = GameSnapshotResultCode.GameNotFound;
            await FTask.CompletedTask;
            return;
        }

        response.ResultCode = GameSnapshotResultCode.Ok;
        response.Step = game.Step;
        response.Score = game.Score;
        GameSessionHelper.FillBoard(game, response.Board);
        GameSessionHelper.FillCandidateQueue(game, response.CandidateQueue);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);
        // 局内叠加层切片回带(opaque,客户端 import):当前对局最近一次搭车存下的切片原文,无则空串。
        response.SliceJson = game.SliceJson ?? string.Empty;

        await FTask.CompletedTask;
    }
}
