using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 进入对局请求(Outer RPC,运行在 Gate Scene)。消息名沿用 GameStart,语义改为「进入对局」=续局或新建。
/// 续局语义:按 playerId 取持久对局,
///   - 有持久 Doc → Rehydrate 恢复中断前盘面/分数/步号/候选 + 发牌器全态(后续发牌与中断前逐位接续),Resumed=true;
///   - 无持久 Doc → 新建对局、签发 seed、建逐局发牌器、发首批 trio,Resumed=false,并落首次存盘。
/// 身份从会话取(GateAccountFlagComponent → Account.PlayerId),请求不携带账号。
/// 局内态是服务端权威进度(data-authority):按 playerId 持久,客户端只持投影。
/// 一会话同时只持一局:进入新对局前清掉旧内存实例(持久 Doc 不动,下次仍可恢复)。
/// </summary>
public sealed class C2G_GameStartRequestHandler : MessageRPC<C2G_GameStartRequest, G2C_GameStartResponse>
{
    protected override async FTask Run(Session session, C2G_GameStartRequest request,
        G2C_GameStartResponse response, Action reply)
    {
        var account = GetSessionAccount(session);
        if (account == null || string.IsNullOrEmpty(account.PlayerId))
        {
            // 未登录无法确定身份:沿框架「非 0 即失败」契约回 ErrorCode,客户端据此提示重登。
            response.ErrorCode = 1;
            return;
        }

        // 一会话只持一局:进入对局前清掉旧内存实例(若有)。持久 Doc 不随之删除——续存的核心是 Doc 仍在。
        var flag = session.GetComponent<GameSessionFlagComponent>();
        if (flag != null)
        {
            GameSession? oldGame = flag.GameSession;
            oldGame?.Dispose();
        }
        else
        {
            flag = session.AddComponent<GameSessionFlagComponent>();
        }

        var persistService = session.Scene.GetComponent<GameSessionServiceComponent>();
        var doc = await GameSessionPersistHelper.Load(persistService, account.PlayerId);

        var game = Entity.Create<GameSession>(session.Scene);
        game.Session = session;
        bool resumed;

        if (doc != null)
        {
            // 续局:从持久 Doc 重建完整权威态 + 发牌器全运行态。
            GameSessionHelper.Rehydrate(game, doc);
            resumed = true;
            Log.Info($"[BlockBlast] EnterGame RESUME playerId={game.PlayerId} gameId={game.GameId} " +
                     $"step={game.Step} score={game.Score} candidates=[{string.Join(",", game.CandidateQueue)}]");
        }
        else
        {
            // 新建:gameId = 实体 Id(服务端唯一签发);seed 由 gameId 派生。
            game.PlayerId = account.PlayerId;
            game.GameId = game.Id;
            GameSessionHelper.Init(game, game.Id);
            resumed = false;
            Log.Info($"[BlockBlast] EnterGame NEW playerId={game.PlayerId} gameId={game.GameId} seed={game.Seed} " +
                     $"initialTrio=[{string.Join(",", game.CandidateQueue)}]");
            // 新建即落首次存盘,使下次进入对局能恢复(即便玩家未落子就退出)。
            await GameSessionPersistHelper.Save(persistService, GameSessionHelper.BuildDoc(game));
        }

        flag.GameSession = game;

        response.GameId = game.GameId;
        response.Seed = game.Seed;
        response.Step = game.Step;
        response.Score = game.Score;
        response.Resumed = resumed;
        GameSessionHelper.FillCandidateQueue(game, response.InitialTrio);
        GameSessionHelper.FillBoard(game, response.Board);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);
    }

    private static Account? GetSessionAccount(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null)
        {
            return null;
        }
        Account account = flag.Account;
        return account;
    }
}
