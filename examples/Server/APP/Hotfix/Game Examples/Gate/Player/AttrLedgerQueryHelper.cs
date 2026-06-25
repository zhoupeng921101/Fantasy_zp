using System.Collections.Generic;
using Fantasy.Async;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家三属性变更流水查询(player_attr_ledger 只读)。
///
/// 单一对外能力 QueryAsync:
///   - 身份从会话取(由 handler 传入,本 helper 不解析会话);
///   - 按 (Account, Timestamp DESC) 主索引取前 Limit 条(SV13 复用 44 §3.2 已建 ix_account_ts_desc);
///   - 支持 kind 过滤(0 = 不过滤,1/2/3 = Coin/Diamond/Stamina,与 PropertyType 枚举错开一位作 sentinel);
///   - 支持 sinceTs 滑动窗口(Timestamp &gt; sinceTs;0 = 不过滤,负值在 handler 校验前已拦);
///   - Limit 上限 100 钳制由 handler 调用前完成(本 helper 只信任入参 Limit ≥ 0);
///   - 字段裁剪 7 字段白名单(timestamp / kind / balanceBefore / balanceAfter / delta / source / reasonRaw,
///     不暴露 ObjectId / SchemaVersion / Account,见 45 §3.2 与 SV15);
///   - hasMore 判定:多查 1 条(Limit+1),若取到 Limit+1 条则 HasMore=true 并截断后 1 条(SV17);
///   - **只读 ledger 集合**:仅 `Find` 系列读 API,**永无** Insert / Update / Delete / FindOneAndUpdate
///     (SV14 + SV18 Code Review 拦)。
///
/// 失败处理:
///   - AttrLedger 句柄 null(MongoDB 启动期不可达)→ 返 ServiceUnavailable + 空列表(SV12);
///   - Mongo 抖动抛 MongoException → catch 返 ServiceUnavailable + 空列表,**不**抛异常断连(SV12);
///   - 入参非法(kind 未知 / sinceTs &lt; 0 / Limit &lt; 0)由 handler 拦,不到本 helper。
///
/// 设计基线:design-docs/45-player-attr-ledger-query.md §三 / §四。
/// </summary>
public static class AttrLedgerQueryHelper
{
    /// <summary>
    /// 协议层 kind 整数(0 = 不过滤,1..7 = 七种 PropertyType + 1 错开一位)→ PropertyType 枚举的映射。
    /// 0 留作 sentinel 表「不过滤」(若直接复用 PropertyType 整数则 Coin=0 与 sentinel 冲突,故协议层错开一位)。
    /// P2 扩到七类:1=Coin / 2=Diamond / 3=Stamina / 4=SoulPower / 5=Piety / 6=GuardianExp / 7=Energy。
    /// 入参 kind 非合法整数 → 由 handler 在调用前拦截返 InvalidRequest,本 helper 不重复校验。
    /// </summary>
    public static bool TryMapKindToPropertyType(int kind, out PropertyType type)
    {
        switch (kind)
        {
            case 1: type = PropertyType.Coin; return true;
            case 2: type = PropertyType.Diamond; return true;
            case 3: type = PropertyType.Stamina; return true;
            case 4: type = PropertyType.SoulPower; return true;
            case 5: type = PropertyType.Piety; return true;
            case 6: type = PropertyType.GuardianExp; return true;
            case 7: type = PropertyType.Energy; return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>
    /// 按 (Account, Timestamp DESC) 索引取 ledger,返回结果码 + entries + hasMore。
    ///
    /// 入参契约(由 handler 校验后传入):
    ///   - account:非空;身份从会话取(handler 已校验)。
    ///   - kindForFilter:null = 不过滤;非 null = 按该 PropertyType 过滤(handler 已映射 + 校验过)。
    ///   - sinceTs:&gt;= 0;0 = 不过滤(等价 ts &gt; 0 几乎不过滤,首次写入时 Timestamp = TimeHelper.Now 都远大于 0)。
    ///   - limit:[0, 100];0 = 直接返空 entries[] + HasMore=false,不查 Mongo(避免无意义请求)。
    ///
    /// 返回:
    ///   - resultCode:Success / ServiceUnavailable。
    ///   - entries:按 Timestamp DESC 排,每行 7 字段(白名单),长度 ∈ [0, limit]。
    ///   - hasMore:true = 取到 limit 条且还有更旧的(查 limit+1 条判);limit=0 时永远 false。
    /// </summary>
    public static async FTask<(AttrLedgerQueryResultCode resultCode, List<AttrLedgerEntry> entries, bool hasMore)> QueryAsync(
        PlayerPropertyServiceComponent service,
        string account,
        PropertyType? kindForFilter,
        long sinceTs,
        int limit)
    {
        var emptyEntries = new List<AttrLedgerEntry>(0);

        var ledger = service.AttrLedger;
        if (ledger == null)
        {
            // MongoDB 启动期不可达 / 未绑句柄(沿 44 + 32 不可达不抛口径,SV12)。
            return (AttrLedgerQueryResultCode.ServiceUnavailable, emptyEntries, false);
        }

        if (limit <= 0)
        {
            // limit=0 提前返空(SV6),避免 Mongo 空查询(MongoDB Limit(0) 语义是「不限」,与此处「返空」相反,必须本地拦)。
            return (AttrLedgerQueryResultCode.Success, emptyEntries, false);
        }

        // 构造 filter:Account == account [AND Timestamp > sinceTs] [AND Kind == kindForFilter]。
        var fb = Builders<PlayerAttrLedgerDoc>.Filter;
        var filter = fb.Eq(x => x.Account, account);
        if (sinceTs > 0L)
        {
            filter &= fb.Gt(x => x.Timestamp, sinceTs);
        }
        if (kindForFilter.HasValue)
        {
            filter &= fb.Eq(x => x.Kind, kindForFilter.Value);
        }

        // 多查 1 条判 hasMore(SV17):取到 limit+1 条则 hasMore=true 并截断后 1 条;
        // 否则 hasMore=false(取到 ≤ limit 条都是「拿尽了」)。
        // 排序按 Timestamp DESC,直接命中 44 §3.2 主索引 ix_account_ts_desc(SV13)。
        var sort = Builders<PlayerAttrLedgerDoc>.Sort.Descending(x => x.Timestamp);
        try
        {
            var docs = await ledger.Find(filter).Sort(sort).Limit(limit + 1).ToListAsync();

            var hasMore = docs.Count > limit;
            if (hasMore)
            {
                docs.RemoveAt(docs.Count - 1);
            }

            var entries = new List<AttrLedgerEntry>(docs.Count);
            foreach (var d in docs)
            {
                // 字段裁剪 7 字段白名单(SV15):不暴露 Id / SchemaVersion / Account。
                var entry = AttrLedgerEntry.Create();
                entry.Timestamp = d.Timestamp;
                entry.Kind = d.Kind;
                entry.BalanceBefore = d.BalanceBefore;
                entry.BalanceAfter = d.BalanceAfter;
                entry.Delta = d.Delta;
                entry.Source = (int)d.Source;
                entry.ReasonRaw = d.ReasonRaw ?? string.Empty;
                entries.Add(entry);
            }

            return (AttrLedgerQueryResultCode.Success, entries, hasMore);
        }
        catch (MongoException e)
        {
            Log.Warning($"AttrLedgerQueryHelper.QueryAsync 失败 account={account} kind={kindForFilter?.ToString() ?? "*"} " +
                        $"sinceTs={sinceTs} limit={limit},err={e.Message}");
            return (AttrLedgerQueryResultCode.ServiceUnavailable, emptyEntries, false);
        }
    }
}
