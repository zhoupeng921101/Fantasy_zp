using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 开局请求(Outer RPC,运行在 Gate Scene)。
/// 服务端建权威 GameSession、签发 seed(客户端不上传)、建逐局发牌器、发首批 trio,回带完整初态。
/// 身份从会话取(GateAccountFlagComponent → Account.PlayerId),请求不携带账号。
/// 一会话同时只持一局:已有则覆盖旧局(旧 GameSession 实例 Dispose)。
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

        // 一会话只持一局:开新局前清掉旧局(若有)。
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

        // 建权威对局实体(挂 Gate Scene)。gameId = 实体 Id(服务端唯一签发);seed 由 gameId 派生(服务端权威、客户端无从预测前不知)。
        var game = Entity.Create<GameSession>(session.Scene);
        game.PlayerId = account.PlayerId;
        game.GameId = game.Id;
        game.Session = session;
        long seed = game.Id;

        GameSessionHelper.Init(game, seed);
        flag.GameSession = game;

        response.GameId = game.GameId;
        response.Seed = game.Seed;
        response.Step = game.Step;
        GameSessionHelper.FillCandidateQueue(game, response.InitialTrio);
        response.GeneratorState = GameSessionHelper.BuildGenState(game);

        Log.Info($"[BlockBlast] GameStart playerId={game.PlayerId} gameId={game.GameId} seed={game.Seed} " +
                 $"initialTrio=[{string.Join(",", response.InitialTrio)}]");

        await FTask.CompletedTask;
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
