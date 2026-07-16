using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 兑换裁决核心逻辑(服务端唯一权威)。
/// 裁决顺序:规整 → 存在/有效 → 过期(服务端时钟) → 全局限量(原子条件递增) → 按账号防重(原子唯一写) → 裁定奖励。
/// 并发原子性:
///   - 全局限量:redeem_counter 用 FindOneAndUpdate(filter Count&lt;Limit, $inc, upsert) 单次原子,达上限不递增(SV8 不超发)。
///   - 按账号防重:redeem_record 以 _id="{account}|{code}" 唯一,重复插入抛 DuplicateKey(SV7 不双发)。
/// 失败分支不写记录、不增计数、不抛异常,以结果码回包(SV10)。
/// 设计基线:design-docs/30-redeem-code-server.md §二/§五。
/// </summary>
public static class RedeemDecisionHelper
{
    /// <summary>MongoDB 重复键错误码。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>
    /// 裁决一次兑换。account 为服务端从会话取得的设备账号(非客户端自报)。
    /// 返回结果码 + 成功时的奖励列表(失败时奖励列表为空)。
    /// </summary>
    public static async FTask<(RedeemResultCode resultCode, int rewardBoxId)> Redeem(
        RedeemServiceComponent self, string account, string rawCode)
    {
        // 服务未就绪(MongoDB 不可达):防重/计数无法持久,返「服务不可用」,不发奖、码保持可兑(不本地放行)。
        // 码表(CodeCache)来自 Luban 配置,独立于 DB;码查不到自然返 InvalidCode,不在此守卫。
        if (self.Records == null || self.Counters == null)
        {
            return (RedeemResultCode.ServiceUnavailable, 0);
        }
        // 守卫后捕获为非空局部量,传给原子操作私有方法(空安全契约在此显式成立)。
        var records = self.Records;
        var counters = self.Counters;

        // 1. 规整权威在服务端:trim + 转大写(SV6)。
        var code = Normalize(rawCode);

        // 2. 空 / 规整后为空 → 码无效(SV3 兜底,不崩)。
        if (string.IsNullOrEmpty(code))
        {
            return (RedeemResultCode.InvalidCode, 0);
        }

        // 3. 查码表:不存在 → 码无效(SV3)。缓存只读静态配置。
        if (!self.CodeCache.TryGetValue(code, out var config))
        {
            return (RedeemResultCode.InvalidCode, 0);
        }

        // 4. 过期判定以服务端时钟为准(SV4)。ExpireUnixMs=0 表示永不过期。
        if (config.ExpireUnixMs > 0 && Fantasy.Helper.TimeHelper.Now >= config.ExpireUnixMs)
        {
            return (RedeemResultCode.Expired, 0);
        }

        // 5. 全局限量:配了上限(>0)才校验。原子条件递增,达上限即失败(SV5/SV8)。
        var limitConsumed = false;
        if (config.GlobalLimit > 0)
        {
            if (!await TryConsumeGlobalSlot(counters, code, config.GlobalLimit))
            {
                return (RedeemResultCode.LimitReached, 0);
            }
            limitConsumed = true;
        }

        // 6. 按账号防重:原子唯一写。重复即「已兑过」,并回滚已占的全局名额(SV2/SV7/SV10)。
        if (!await TryWriteRecord(records, account, code))
        {
            if (limitConsumed)
            {
                // 这次请求是重复兑换,却已占了一个名额,补偿性归还(失败分支不占名额, SV10)。
                await ReleaseGlobalSlot(counters, code);
            }
            return (RedeemResultCode.AlreadyRedeemed, 0);
        }

        // 7. 服务端权威发奖:按码配置的奖励盒整盒发放(货币经 num→PropertyType 落账 + 推送 / 道具入背包 + 推送),
        //    与全局固定奖励发放同源(RewardBoxServiceHelper,「发固定奖励一律调发奖器」),不再由客户端本地发奖。
        //    发奖 best-effort(GrantBoxAsync 内部逐项失败记 Error 不抛);记录先于发奖,极小概率丢奖窗口不做登录补领(同塔罗发奖范式)。
        //    热路径气味:兑换低频(每玩家偶发点击),每次盒内若干笔 ChangeProperty 原子写 + 推送,量小、成本可接受,不降频(server-perf)。
        var propertyService = self.Scene.GetComponent<PlayerPropertyServiceComponent>();
        if (propertyService != null)
        {
            await RewardBoxServiceHelper.GrantBoxAsync(self.Scene, propertyService, account, config.RewardBoxId, $"redeem:{code}");
        }
        else
        {
            Log.Error($"兑换发奖:Scene 无 PlayerPropertyServiceComponent,奖励未发放 account={account} code={code} box={config.RewardBoxId}");
        }

        return (RedeemResultCode.Success, config.RewardBoxId);
    }

    /// <summary>
    /// 服务端规整口径:去首尾空白 + 转大写(SV6)。null/纯空白 → 空串。
    /// </summary>
    public static string Normalize(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return string.Empty;
        }
        return rawCode.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// 原子占用一个全局名额:FindOneAndUpdate(filter Count&lt;limit, $inc Count 1, upsert)。
    /// 成功(计数在上限内递增/首次创建)返回 true;达上限(无匹配且 upsert 主键冲突)返回 false。
    /// </summary>
    private static async FTask<bool> TryConsumeGlobalSlot(IMongoCollection<RedeemCounterDoc> counters, string code, int limit)
    {
        var filter = Builders<RedeemCounterDoc>.Filter.And(
            Builders<RedeemCounterDoc>.Filter.Eq(x => x.Code, code),
            Builders<RedeemCounterDoc>.Filter.Lt(x => x.Count, limit));
        var update = Builders<RedeemCounterDoc>.Update.Inc(x => x.Count, 1);
        var options = new FindOneAndUpdateOptions<RedeemCounterDoc>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            // 文档不存在 → upsert 用 filter 的相等谓词(Code)构造新文档并 +1(首次 Count=1)。
            // 文档存在且 Count<limit → 原子 +1。
            // 文档存在且 Count>=limit → filter 不匹配,upsert 试图按 _id=code 插入 → 主键冲突(11000)→ 达上限。
            await counters.FindOneAndUpdateAsync(filter, update, options);
            return true;
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return false;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <summary>
    /// 归还一个全局名额(补偿:重复兑换误占名额时回滚)。条件递增 -1 且不降到负数。
    /// </summary>
    private static async FTask ReleaseGlobalSlot(IMongoCollection<RedeemCounterDoc> counters, string code)
    {
        var filter = Builders<RedeemCounterDoc>.Filter.And(
            Builders<RedeemCounterDoc>.Filter.Eq(x => x.Code, code),
            Builders<RedeemCounterDoc>.Filter.Gt(x => x.Count, 0));
        var update = Builders<RedeemCounterDoc>.Update.Inc(x => x.Count, -1);
        await counters.UpdateOneAsync(filter, update);
    }

    /// <summary>
    /// 原子写入按账号防重记录:_id="{account}|{code}" 唯一。
    /// 首次写入返回 true;重复(DuplicateKey)返回 false(= 已兑过)。
    /// </summary>
    private static async FTask<bool> TryWriteRecord(IMongoCollection<RedeemRecordDoc> records, string account, string code)
    {
        var doc = new RedeemRecordDoc
        {
            UniqueKey = $"{account}|{code}",
            Account = account,
            Code = code,
            RedeemedUnixMs = Fantasy.Helper.TimeHelper.Now
        };

        try
        {
            await records.InsertOneAsync(doc);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return false;
        }
    }

    // ── 清档 ──────────────────────────────────────────────────

    /// <summary>
    /// 清档·删除某账号在 redeem_record 的全部已兑记录(按玩家身份 Account 删,非 _id)。
    /// 一个账号每兑一个码各占一行(_id = "{account}|{code}"),故 1:N → DeleteMany。
    /// 删后该账号此前已兑的码回到「可再兑」态(在该码自身未过期 / 未达全局上限前提下)。
    /// **不**触碰 redeem_counter:其 _id=Code,是按码的全局发放计数(无 Account 字段),属全局共享状态——
    /// 递减它会凭空归还其他玩家已占的全局名额(限量码被超发),不在 per-player 清档范围。
    /// 幂等:0 匹配(本就未兑过)同样视为成功。返回 true=成功(含本就无行);false=MongoDB 不可达 / 异常。
    /// </summary>
    public static async FTask<bool> ClearByAccount(RedeemServiceComponent self, string account)
    {
        if (self.Records == null)
        {
            return false;
        }
        if (string.IsNullOrEmpty(account))
        {
            return true;
        }

        try
        {
            var filter = Builders<RedeemRecordDoc>.Filter.Eq(x => x.Account, account);
            var result = await self.Records.DeleteManyAsync(filter);
            Log.Debug($"Redeem 清档删除兑换记录 account={account} deletedCount={result.DeletedCount}");
            return true;
        }
        catch (MongoException e)
        {
            Log.Warning($"RedeemDecisionHelper.ClearByAccount 失败 account={account},err={e.Message}");
            return false;
        }
    }
}
