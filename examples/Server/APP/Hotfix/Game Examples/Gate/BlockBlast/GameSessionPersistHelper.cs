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

    /// <summary>存盘防抖:普通落子最多每 N 步落盘一次(降低对局写库频率,头号写源)。</summary>
    private const int PersistEveryNSteps = 5;

    /// <summary>存盘防抖:普通落子最多每 T 毫秒落盘一次。</summary>
    private const long PersistIntervalMs = 3000;

    /// <summary>
    /// 防抖存盘:仅在 force(消行 / 清盘 / 清行列等关键事件)、距上次存盘 ≥N 步、或 ≥T 毫秒时才真写库;
    /// 否则跳过本次写库(内存态已推进,到下个存盘点或断线 flush 时补上)。
    /// 写库前同步快照当前态并乐观标记「已落盘到当前步」——存盘是全量快照(非增量),即便本次写失败,后续任一成功写自愈。
    /// 权衡:硬崩溃最坏丢单玩家最近一个防抖窗口(≤N 步 / ≤T 毫秒)的落子;消行必存不丢;优雅断线由 DestroySystem flush 补上。
    /// </summary>
    public static async FTask SaveIfDue(GameSessionServiceComponent service, GameSession game, long nowMs, bool force)
    {
        if (game == null)
        {
            return;
        }
        bool due = force
                   || game.Step - game.LastPersistedStep >= PersistEveryNSteps
                   || nowMs - game.LastPersistUnixMs >= PersistIntervalMs;
        if (!due)
        {
            return;
        }
        int stepSnapshot = game.Step;
        var doc = GameSessionHelper.BuildDoc(game); // 同步快照当前态(BuildDoc 内无 await,原子)
        // 仅在**确认写库成功**后推进已落盘步号:写失败不推进,断线 flush 的脏检查(Step > LastPersistedStep)仍能兜底
        // (否则 force 存盘瞬时失败 + 随即断线会漏掉该步)。guard stepSnapshot > 现值:防 await 期间并发写乱序完成造成回退。
        if (await Save(service, doc) && stepSnapshot > game.LastPersistedStep)
        {
            game.LastPersistedStep = stepSnapshot;
            game.LastPersistUnixMs = nowMs;
        }
    }

    /// <summary>
    /// flush 防抖窗口内未落盘的步(dirty=Step>LastPersistedStep 才写)。供断线(DestroySystem)与同会话重进对局(GameStart 弃旧局前)
    /// 复用同一兜底口径,避免绕过防抖直接销毁内存局丢步。仅确认成功才推进已落盘步号。
    /// </summary>
    public static async FTask FlushIfDirty(GameSessionServiceComponent service, GameSession game)
    {
        if (game == null || game.Step <= game.LastPersistedStep)
        {
            return;
        }
        int stepSnapshot = game.Step;
        var doc = GameSessionHelper.BuildDoc(game);
        if (await Save(service, doc) && stepSnapshot > game.LastPersistedStep)
        {
            game.LastPersistedStep = stepSnapshot;
            game.LastPersistUnixMs = Fantasy.Helper.TimeHelper.Now;
        }
    }

    /// <summary>
    /// 覆盖式存盘(_id=playerId upsert,整体替换)。doc 由 GameSessionHelper.BuildDoc 组装。
    /// 返回是否**确认写库成功**:不可达 / 异常吞掉(Warning 留痕)返 false —— 防抖存盘据此仅在成功时推进已落盘步号,
    /// 使写失败后的断线 flush 脏检查仍能兜底(存盘失败本身不回滚落子裁决,权威态已在内存推进)。
    /// </summary>
    public static async FTask<bool> Save(GameSessionServiceComponent service, GameSessionDoc doc)
    {
        if (service?.Sessions == null || doc == null || string.IsNullOrEmpty(doc.PlayerId))
        {
            return false;
        }

        doc.LastUpdateUnixMs = Fantasy.Helper.TimeHelper.Now;

        try
        {
            var filter = Builders<GameSessionDoc>.Filter.Eq(x => x.PlayerId, doc.PlayerId);
            var options = new ReplaceOptions { IsUpsert = true };
            await service.Sessions.ReplaceOneAsync(filter, doc, options);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"GameSessionPersistHelper.Save 失败 playerId={doc.PlayerId} gameId={doc.GameId} err={e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 终局删档(按 playerId 删除持久对局)。终局即结束本局:删除 Doc 后,下次进入对局 Load 返 null → 走新建(Resumed=false),
    /// 不复活已结束局。删除而非置 ended 标志:Load「无档=新建」语义已成立,删档即终结,无需在 Doc/GameStart 增态。
    /// 不可达 / 异常静默吞掉(Warning 留痕):删档失败不应让终局裁决回滚(内存实例已 Dispose、入榜已提交);
    /// 残留 Doc 下次进入会被当续局恢复成已结束盘面(jam 盘),玩家再落子即再触发终局删档自愈,不造成错误进度。
    /// </summary>
    public static async FTask Delete(GameSessionServiceComponent? service, string playerId)
    {
        if (service?.Sessions == null || string.IsNullOrEmpty(playerId))
        {
            return;
        }

        try
        {
            var filter = Builders<GameSessionDoc>.Filter.Eq(x => x.PlayerId, playerId);
            await service.Sessions.DeleteOneAsync(filter);
        }
        catch (Exception e)
        {
            Log.Warning($"GameSessionPersistHelper.Delete 失败 playerId={playerId} err={e.Message}");
        }
    }
}
