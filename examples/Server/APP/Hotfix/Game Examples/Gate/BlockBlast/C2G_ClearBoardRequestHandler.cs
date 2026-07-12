using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 清盘请求(Outer RPC,运行在 Gate Scene,调试用)。清空当前对局盘面全部已占格,
/// 作为一次 board-mutating 动作推进 Step(与落子 / 消除道具同一步号轴),但不消耗候选、不推进发牌调度、
/// 不续发、不触发全清判定 / 不给全清奖;分数与候选 / 发牌器态不变。
///
/// 与消除道具(C2G_ClearTool)同范式取会话对局并回带最新权威态,区别:清全盘而非一行一列、不扣体力、
/// 无 BaseStep 幂等(GM 一次性手点,重复清已空盘无害,仅再推进 Step)。响应回带新盘面 + 发牌器态供客户端重投影。
/// 请求 SliceJson(客户端清后局内叠加层不透明切片)搭车存盘,服务端只搬运不解析。
/// 所有结果以 ResultCode 回包,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_ClearBoardRequestHandler : MessageRPC<C2G_ClearBoardRequest, G2C_ClearBoardResponse>
{
    protected override async FTask Run(Session session, C2G_ClearBoardRequest request,
        G2C_ClearBoardResponse response, Action reply)
    {
        var game = ResolveGame(session, request.GameId, out var resultCode);
        if (game == null)
        {
            response.ResultCode = resultCode;
            // 无对局 / 未登录:回带空态(Step/Score 默认 0,Board/GeneratorState 留空)。
            await FTask.CompletedTask;
            return;
        }

        // 清全盘 + 推进 Step(不消耗候选、不推进发牌、不续发)。
        int clearedCells = GameSessionHelper.ClearBoard(game);
        response.ResultCode = ClearBoardResultCode.Cleared;
        response.ClearedCells = clearedCells;

        // 搭车的局内叠加层切片(opaque,不解析)写进实体,随 BuildDoc 存盘;供续局 / 快照回带。
        game.SliceJson = request.SliceJson ?? string.Empty;

        // 清盘成功后存盘:盘面 / 步号落 Doc,供续局 / 快照恢复(与落子 / 消除道具成功后存盘同口径)。
        var persistService = session.Scene.GetComponent<GameSessionServiceComponent>();
        // 清盘是关键事件,force 立即存(并推进防抖的已落盘步号,保持状态一致)。
        await GameSessionPersistHelper.SaveIfDue(persistService, game, Fantasy.Helper.TimeHelper.Now, force: true);

        // 回带最新权威态供客户端重投影(契约对齐 G2C_ClearToolResponse)。
        response.Step = game.Step;
        response.Score = game.Score;
        GameSessionHelper.FillBoard(game, response.Board);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);

        Log.Info($"[BlockBlast] GM ClearBoard gameId={game.GameId} step={game.Step} cleared={clearedCells}");
    }

    /// <summary>从会话取当前权威对局,并校验 gameId 一致。返回 null 时 out 给出对应结果码。</summary>
    private static GameSession? ResolveGame(Session session, long gameId, out ClearBoardResultCode resultCode)
    {
        var accountFlag = session.GetComponent<GateAccountFlagComponent>();
        if (accountFlag == null)
        {
            resultCode = ClearBoardResultCode.NotLoggedIn;
            return null;
        }

        var gameFlag = session.GetComponent<GameSessionFlagComponent>();
        GameSession? game = gameFlag?.GameSession;
        if (game == null || game.GameId != gameId)
        {
            resultCode = ClearBoardResultCode.GameNotFound;
            return null;
        }

        resultCode = ClearBoardResultCode.Cleared;
        return game;
    }
}
