using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家三属性变更审计流水(player_attr_ledger)写入与 source 映射(设计 44 §3.3 + §3.4)。
///
/// 两类对外能力:
///   1. MapReasonToSource — 单一映射函数(全部 ChangeProperty 调用方共用):
///      把调用方传入的 reason 字符串映射成 AttrChangeSource 枚举,未命中返 Unknown(永不抛,SV7/SV8)。
///      **大小写敏感、精确前缀匹配**(SV9,防误归类),新增业务 reason 必登记此处。
///   2. AppendAsync — ledger 旁路追加写(挂在 37 ChangeProperty 写库成功裁决后,return 前调用):
///      字段不变量写入前 assert(SV10):BalanceAfter == BalanceBefore + Delta + 余额非负;
///      InsertOne 一次写入,**永不** update / delete(SV14 Code Review 必拦);
///      MongoDB 不可达 / 写超时 → Warning 不抛,**不**回滚 players 余额(SV11,设计 44 §3.4 决策);
///      AttrLedger 句柄 null(MongoDB 不可达启动时未绑)→ 静默跳过,不阻断 ChangeProperty 返回。
///
/// 时序契约:在 37 PlayerPropertyServiceHelper.ChangeProperty 内 `FindOneAndUpdate 成功 → AppendAsync → return`,
/// 调用方拿到 ChangeProperty 返回后再 SendDeltaPushTo → 推送给客户端。
/// 这保证 ledger 永远在推送之前写入,推送阶段抛异常也不漏 ledger(SV11,设计 44 §3.4 顺序)。
///
/// 反作弊红线(SV18 Code Review 重点,设计 44 §3.4 + §5.4):
///   - ledger 写入**不**进 37 FindOneAndUpdate 原子边界(旁路追加,余额已成功定格);
///   - 失败分支(余额不足 / 上界溢出 / UnknownType / FindOneAndUpdate 失败)**永不**调 AppendAsync;
///   - 业务系统**不**自报 source(全部走 MapReasonToSource 单一函数,防新增路径漏登记);
///   - ledger 永不 update / delete(grep 全工程拦 PlayerAttrLedgerDoc / "player_attr_ledger" 的修改写)。
///
/// 设计基线:design-docs/44-player-attr-ledger.md §3.3 + §3.4 + §3.5。
/// </summary>
public static class AttrLedgerHelper
{
    /// <summary>本子单 ledger schema 版本(设计 44 §3.1)。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// reason 字符串 → AttrChangeSource 单一映射函数(设计 44 §3.3 末段)。
    ///
    /// 匹配策略:**前缀精确匹配,大小写敏感**(SV9 防误归类:`"player_rename"` 中 / `"Player_Rename"` 不中)。
    /// 未命中 = Unknown 兜底(reasonRaw 原文仍保留供运营 ad-hoc 查 + 排查未登记调用方)。
    ///
    /// 新增业务路径接 37 ChangeProperty 时,**必须**在本函数登记新映射,
    /// 否则其 reason 全部走 Unknown(SV18 Code Review 拦)。
    ///
    /// **永不抛异常**(SV7):reason null → 当空串处理 → Unknown。
    /// </summary>
    public static AttrChangeSource MapReasonToSource(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return AttrChangeSource.Unknown;
        }

        // 精确匹配优先(完整字符串相等);未中再走前缀匹配。
        // 大小写敏感:用 Ordinal(非 OrdinalIgnoreCase),防 "Player_Rename" 误中 ChangeNameSpend。
        if (reason.Equals("player_rename", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.ChangeNameSpend;
        }

        // 前缀匹配:业务侧 reason 带子分类(如 "mail_claim_456" / "redeem_code_X" / "rank_settle_1_3"),
        // 子分类供运营按 reasonRaw 二次过滤,source 维度按前缀大类归并。
        // 顺序:更长前缀在前,防短前缀误中(本子单各前缀互斥,顺序无歧义,仍按 design 表顺保持可读)。
        if (reason.StartsWith("mail_claim", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.MailClaim;
        }
        if (reason.StartsWith("redeem_code", System.StringComparison.Ordinal) ||
            reason.StartsWith("redeem_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.RedeemCode;
        }
        if (reason.StartsWith("rank_settle", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.RankSettleReward;
        }
        if (reason.StartsWith("activity_reward", System.StringComparison.Ordinal) ||
            reason.StartsWith("activity_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.ActivityReward;
        }
        // Tier 2+ 增强枚举占位(各业务刀接入时取消注释):
        if (reason.StartsWith("gameplay_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.GameplayConsume;
        }
        if (reason.StartsWith("shop_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.ShopPurchase;
        }
        if (reason.StartsWith("admin_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.AdminGrant;
        }
        if (reason.StartsWith("refund_", System.StringComparison.Ordinal))
        {
            return AttrChangeSource.Refund;
        }

        return AttrChangeSource.Unknown;
    }

    /// <summary>
    /// 追加一行 ledger(设计 44 §3.4 时序:写库成功后、推送前,挂在 37 ChangeProperty 内 return 之前)。
    ///
    /// 入参:
    ///   - service:PlayerPropertyServiceComponent(取 AttrLedger 句柄)
    ///   - account:UUID
    ///   - kind:属性种类(三类之一)
    ///   - balanceBefore / balanceAfter / delta:字段不变量(此处 assert)
    ///   - reasonRaw:调用方原 reason 字符串(map 出 source + 落 ReasonRaw 字段保留原文)
    ///   - timestampMs:应用端写库成功时刻(= 37 内 nowMs;统一应用端为权威,不读 DateTime.UtcNow,可测)
    ///
    /// 失败处理(SV11):
    ///   - AttrLedger 句柄 null(MongoDB 启动期不可达) → 静默跳过,不抛(余额已成功定格);
    ///   - InsertOneAsync 抛 MongoException(Mongo 抖动 / 写超时) → Warning 不抛,**不**回滚 players;
    ///   - 字段不变量 assert 失败(实现 bug,balanceAfter != balanceBefore + delta) → Error 不写(不污染审计)。
    /// </summary>
    public static async FTask AppendAsync(
        PlayerPropertyServiceComponent service,
        string account,
        PropertyType kind,
        long balanceBefore,
        long balanceAfter,
        long delta,
        string reasonRaw,
        long timestampMs)
    {
        // 字段不变量(SV10):写入前 assert,违反 = 实现 bug,不写(写了反污染审计)+ Error 告警。
        if (balanceAfter != balanceBefore + delta || balanceBefore < 0L || balanceAfter < 0L)
        {
            Log.Error($"AttrLedgerHelper.AppendAsync 字段不变量违反,account={account} kind={kind} " +
                      $"before={balanceBefore} after={balanceAfter} delta={delta} reason='{reasonRaw}'。拒写 ledger。");
            await FTask.CompletedTask;
            return;
        }

        var ledger = service.AttrLedger;
        if (ledger == null)
        {
            // MongoDB 启动期不可达 / 未绑句柄(理论上若 Players 已绑、此处 AttrLedger 也应已绑,防御性处理)。
            // 不抛,余额已成功定格,沿 ledger 失败「不回滚 players + 仅告警」基线(设计 44 §3.4)。
            // 此处不重复 Warning(启动期已 Warning 一次)。
            await FTask.CompletedTask;
            return;
        }

        var source = MapReasonToSource(reasonRaw);
        var doc = new PlayerAttrLedgerDoc
        {
            Id = ObjectId.GenerateNewId(),
            Timestamp = timestampMs,
            Account = account,
            Kind = kind,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Delta = delta,
            Source = source,
            ReasonRaw = reasonRaw ?? string.Empty,
            SchemaVersion = CurrentSchemaVersion,
        };

        try
        {
            await ledger.InsertOneAsync(doc);
            Log.Debug($"AttrLedger 追加成功 account={account} kind={kind} delta={delta} before={balanceBefore} after={balanceAfter} source={source} reason='{reasonRaw}' ts={timestampMs}");
        }
        catch (MongoException e)
        {
            // ledger 写失败:不回滚 players(余额已成功定格,玩家无感)+ Warning 告警(运营据日志补查缺口)。
            // 设计 44 §3.4 决策:服务可用性 > 审计完整性(Tier 2 取后者,Tier 2+ 加重试队列 O6)。
            Log.Warning($"AttrLedger 追加失败(Mongo 抖动) account={account} kind={kind} delta={delta} " +
                        $"before={balanceBefore} after={balanceAfter} source={source} reason='{reasonRaw}' ts={timestampMs},err={e.Message}");
        }
    }

    /// <summary>
    /// 清档·删除某账号在 player_attr_ledger 的全部流水行(按玩家身份 Account 删,非 _id)。
    /// 一个账号每笔成功属性变更各占一行(_id = Mongo ObjectId 自增),故 1:N → DeleteMany。
    ///
    /// 关于「ledger 永不 update / delete」不变量:该不变量针对**业务运行期路径**(ChangeProperty / 发奖 / 退款),
    /// 防审计流水被篡改。清档·重置为新手是用户主动发起、把账号整体回退到全新态的边界操作(账号身份保留、
    /// 玩法数据全清),其语义内含「该账号此前的变更历史一并作废」,是该不变量的明示例外;
    /// 仅本清档路径 + 上层 ClearPlayerData handler 可调,业务路径仍禁删(grep 守卫的目标是业务路径误删,非此入口)。
    ///
    /// 幂等:0 匹配(本就无流水)同样视为成功。返回 true=成功(含本就无行);false=MongoDB 不可达 / 异常。
    /// </summary>
    public static async FTask<bool> ClearByAccount(PlayerPropertyServiceComponent service, string account)
    {
        var ledger = service.AttrLedger;
        if (ledger == null)
        {
            return false;
        }
        if (string.IsNullOrEmpty(account))
        {
            return true;
        }

        try
        {
            var filter = Builders<PlayerAttrLedgerDoc>.Filter.Eq(x => x.Account, account);
            var result = await ledger.DeleteManyAsync(filter);
            Log.Debug($"AttrLedger 清档删除流水 account={account} deletedCount={result.DeletedCount}");
            return true;
        }
        catch (MongoException e)
        {
            Log.Warning($"AttrLedgerHelper.ClearByAccount 失败 account={account},err={e.Message}");
            return false;
        }
    }
}
