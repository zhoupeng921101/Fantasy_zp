using System;
using Fantasy.Async;
using Fantasy.Network.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// GM 清号(Inner RPC,HttpGift 场景转发到 Gate 场景执行)。对任意账号做**完整清号** —— 复用 ClearPlayerDataHelper.ClearAll
/// (与客户端 GM 清号 C2G_ClearPlayerData 同一份编排):重置玩家文档为新手态 + 删在局对局档 / 活动进度 / 邮件 / 排行榜分数 / 兑换记录 / 属性流水。
/// HTTP GM 无会话,身份不从会话取:accountId 由请求带,playerId 从 players 文档(_id=accountId)读出。
/// 目标玩家在线时不强制其重登,完整重置在服务端已生效;客户端需重登从新态重建(同客户端清号)。
/// Response.Code:0=成功;非0=ClearPlayerDataResultCode 值或 GM 侧失败(-1 空账号 / -2 服务不可用)。
/// </summary>
public sealed class Http2G_GmResetPlayerRequestHandler
    : AddressRPC<Scene, Http2G_GmResetPlayerRequest, G2Http_GmResetPlayerResponse>
{
    protected override async FTask Run(Scene scene, Http2G_GmResetPlayerRequest request,
        G2Http_GmResetPlayerResponse response, Action reply)
    {
        if (string.IsNullOrEmpty(request.AccountId))
        {
            response.Code = -1;
            response.Message = "empty accountId";
            return;
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            response.Code = -2;
            response.Message = "player service unavailable";
            return;
        }

        // 取 playerId(在局对局档主键):HTTP GM 无会话,从 players 文档(_id=accountId)读 PlayerDoc.PlayerId。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, request.AccountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            response.Code = -2;
            response.Message = $"read player doc failed: {e.Message}";
            return;
        }
        if (doc == null)
        {
            // 账号无 players 文档(从未登录)→ 无 per-player 数据可清,幂等成功。
            response.Code = 0;
            response.Message = "no player doc (never logged in), nothing to clear";
            Log.Info($"GM 清号:account={request.AccountId} 无 players 文档(未登录过),视为已清。");
            return;
        }

        var resultCode = await ClearPlayerDataHelper.ClearAll(scene, request.AccountId, doc.PlayerId);
        response.Code = (int)resultCode;
        response.Message = resultCode.ToString();
        if (resultCode == ClearPlayerDataResultCode.Success)
        {
            Log.Info($"GM 清号成功 account={request.AccountId} playerId={doc.PlayerId}(在线玩家需重登从新态重建)。");
        }
        else
        {
            Log.Warning($"GM 清号未成功 account={request.AccountId} code={resultCode}。");
        }
    }
}
