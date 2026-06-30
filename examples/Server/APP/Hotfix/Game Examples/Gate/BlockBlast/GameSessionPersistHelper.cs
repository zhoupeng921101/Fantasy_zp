using System;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// Block Blast 对局持久 I/O helper:封装按 playerId 的存盘(upsert 覆盖式)与读盘(单点 get)。
/// 服务端是局内态唯一写者(客户端只发输入、不上传局内态),单玩家单局 → upsert 覆盖即可,无版本冲突裁决。
/// MongoDB 不可达(Sessions == null)时存盘静默跳过、读盘返 null(等价新建对局),不阻断玩法。
/// </summary>
public static class GameSessionPersistHelper
{
    /// <summary>按 playerId 读当前持久对局;无档 / 不可达 / 异常 → 返 null(调用方据此新建对局)。</summary>
    public static async FTask<GameSessionDoc?> Load(GameSessionServiceComponent service, string playerId)
    {
        if (service?.Sessions == null || string.IsNullOrEmpty(playerId))
        {
            return null;
        }

        try
        {
            var filter = Builders<GameSessionDoc>.Filter.Eq(x => x.PlayerId, playerId);
            return await service.Sessions.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception e)
        {
            Log.Warning($"GameSessionPersistHelper.Load 失败 playerId={playerId} err={e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 覆盖式存盘(_id=playerId upsert,整体替换)。doc 由 GameSessionHelper.BuildDoc 组装。
    /// 不可达 / 异常静默吞掉(Warning 留痕):存盘失败不应让落子裁决回滚(权威态已在内存推进,下次存盘补上)。
    /// </summary>
    public static async FTask Save(GameSessionServiceComponent service, GameSessionDoc doc)
    {
        if (service?.Sessions == null || doc == null || string.IsNullOrEmpty(doc.PlayerId))
        {
            return;
        }

        doc.LastUpdateUnixMs = Fantasy.Helper.TimeHelper.Now;

        try
        {
            var filter = Builders<GameSessionDoc>.Filter.Eq(x => x.PlayerId, doc.PlayerId);
            var options = new ReplaceOptions { IsUpsert = true };
            await service.Sessions.ReplaceOneAsync(filter, doc, options);
        }
        catch (Exception e)
        {
            Log.Warning($"GameSessionPersistHelper.Save 失败 playerId={doc.PlayerId} gameId={doc.GameId} err={e.Message}");
        }
    }
}
