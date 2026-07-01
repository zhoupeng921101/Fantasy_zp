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
///
/// 落子体力服务端派生(第 2 批,范式同 C2G_ClearToolRequestHandler / C2G_DeliverOrderRequestHandler):
/// 成功落子(StepAdvanced)在服务端派生体力落账 —— 扣 PlaceCost、消行按行列数返还(夹 EnergyCap 软上限),
/// 不再由客户端 C2G_PropertyChange 自报,普通(不消行)落子从此零 PropertyChange 往返。
/// 逐位复刻客户端 MergeOrderState 顺序(先扣 PlaceCost 再返还):
///   target = eliminatedLines>0 ? min(EnergyCap, (E - PlaceCost) + eliminatedLines) : (E - PlaceCost),E=落子前权威体力。
/// netDelta = target - E 经 ChangeProperty(serverAuthoritative=true) 原子落账,response.NewEnergy = 落账后余额并推送对齐 HUD。
/// 幂等免疫双扣:GameSessionHelper.Place 在任何 await 之前同步 Step++,并发同 baseStep 的第二条命中 baseStep &lt; Step
/// 幂等分支、不重跑 Place、不重派生体力,故不需 ClearTool 那样的在途守卫(前提:体力派生放在 Place() 之后的 StepAdvanced 分支内)。
/// 本批只派生体力;落子消行的 Soul/Piety/GuardianExp(融合元经济)服务端未建模,仍由客户端自报,本批不碰。
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

            // 仅在权威态真推进(成功落子)时派生体力 + 存盘:盘面/分数/步号/发牌器全态落 Doc,供续局恢复。
            // 存盘失败不回滚裁决(权威态已在内存推进),下次落子存盘补上(见 GameSessionPersistHelper.Save)。
            if (response.ResultCode == PlaceResultCode.StepAdvanced)
            {
                var persistService = session.Scene.GetComponent<GameSessionServiceComponent>();

                // 落子体力服务端派生:放在 IsGameOver 判定之前,故触发终局那一手也照常派生体力。
                await DerivePlaceEnergy(session, game, eliminatedLines, response);

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

    /// <summary>
    /// 成功落子的体力服务端派生落账(逐位复刻客户端 MergeOrderState:先扣 PlaceCost,再消行返还夹 EnergyCap)。
    ///
    /// 顺序保证 netDelta 基于的 E 与落账时一致(不漂移):
    ///   ReadEnergyAuthoritative(await 结算恢复取 E)→ 同步算 target/netDelta(无 await)→ await ChangeProperty($inc netDelta)。
    /// 读 E 与 ChangeProperty 之间无 await,单线程 Scene 仅在 await 点交错;ChangeProperty 内部恢复结算因同 nowMs 变 no-op,
    /// 故 $inc(netDelta) 落在 E 上、命中 target。target = eliminatedLines>0 ? min(EnergyCap,(E-PlaceCost)+lines) : (E-PlaceCost)。
    ///
    /// 结果写 response.NewEnergy(供客户端对账,覆盖乐观值):
    ///   - Success:NewEnergy=落账后余额 + SendDeltaPushTo 对齐在线 HUD;
    ///   - NotEnough(体力 &lt; PlaceCost;正常不会,客户端已 gate):不使落子失败(棋盘已推进不回滚),记日志,
    ///     NewEnergy=当前权威余额(未扣),不推送,客户端以此把乐观扣回正;
    ///   - 服务不可用 / 读失败:记日志,NewEnergy 留默认 0(客户端忽略此值,靠属性推送/快照对齐),不推送。棋盘已推进不回滚。
    /// </summary>
    private static async FTask DerivePlaceEnergy(Session session, GameSession game,
        int eliminatedLines, G2C_PlaceResponse response)
    {
        var accountId = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(accountId))
        {
            // ResolveGame 已保证有 GateAccountFlagComponent,理论不达此;防御性:不派生体力,NewEnergy 留 0。
            Log.Warning($"[BlockBlast] Place 派生体力跳过(会话无账号) gameId={game.GameId} step={game.Step}");
            return;
        }

        // 读落子前权威体力 E(先结算被动恢复,与 ChangeProperty 同 nowMs 口径)。读 → 算 netDelta 之间不得有 await。
        var (ok, energyBefore) = await PlayerPropertyServiceHelper.ReadEnergyAuthoritative(session.Scene, accountId);
        if (!ok)
        {
            // 体力服务不可用 / 未首登:不派生(棋盘已推进不回滚),NewEnergy 留 0,客户端靠属性推送/快照对齐。
            Log.Warning($"[BlockBlast] Place 派生体力跳过(体力读取失败) account={accountId} gameId={game.GameId} step={game.Step}");
            return;
        }

        // 同步算 target/netDelta(无 await):先扣 PlaceCost,消行按行列数返还夹 EnergyCap 软上限。
        long placeCost = MergeOrderConfigServer.PlaceCost;
        long energyCap = MergeOrderConfigServer.EnergyCap;
        long afterSpend = energyBefore - placeCost;
        long target = eliminatedLines > 0
            ? Math.Min(energyCap, afterSpend + eliminatedLines)
            : afterSpend;
        long netDelta = target - energyBefore;

        var reason = $"place:g{game.GameId}:s{game.Step}";
        var (energyResult, energyAfter) = await PlayerPropertyServiceHelper.ChangeProperty(
            session.Scene, accountId, PropertyType.Energy, netDelta, reason, serverAuthoritative: true);

        if (energyResult == PropertyChangeResultCode.Success)
        {
            response.NewEnergy = energyAfter;
            // 推送体力增量(与 ClearTool / DeliverOrder 同范式,让在线会话 HUD 对齐)。
            PlayerPropertyServiceHelper.SendDeltaPushTo(session.Scene, accountId, PropertyType.Energy, energyAfter, reason);
            Log.Debug($"[BlockBlast] Place 派生体力 account={accountId} gameId={game.GameId} step={game.Step} " +
                      $"lines={eliminatedLines} E={energyBefore} target={target} netDelta={netDelta} newEnergy={energyAfter}");
            return;
        }

        if (energyResult == PropertyChangeResultCode.NotEnough)
        {
            // 体力不足(< PlaceCost):正常不会(客户端 CanAffordPlace 已 gate)。不使落子失败(棋盘已推进不回滚);
            // NewEnergy = 当前权威余额(未扣,ChangeProperty NotEnough 时返当前实际余额),客户端以此把乐观扣回正。
            response.NewEnergy = energyAfter;
            Log.Warning($"[BlockBlast] Place 体力不足未落账 account={accountId} gameId={game.GameId} step={game.Step} " +
                        $"E={energyBefore} placeCost={placeCost} netDelta={netDelta} current={energyAfter}(棋盘已推进不回滚)");
            return;
        }

        // ServiceUnavailable / 其它:不推送,NewEnergy 留 0(客户端靠属性推送/快照对齐)。棋盘已推进不回滚。
        Log.Warning($"[BlockBlast] Place 派生体力落账失败 account={accountId} gameId={game.GameId} step={game.Step} " +
                    $"netDelta={netDelta} result={energyResult} reason='{reason}'");
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
