using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 玩家属性账本裁决核心(服务端唯一权威)。
///
/// 三类对外能力,共用同一套校验 + 写库 + 推送(plan D7 + §3.5,业务系统无法因「内部调用」绕过反作弊):
///   1. InitOrLoad — 35 登录处理链调用:首登 setOnInsert 写初始记录 + 读三属性当前余额,
///      与 35 accounts upsert 同事务模式但落 players 集合(SV3/SV5);
///   2. ChangeProperty — PropertyChangeRequest handler / 服务端进程内变更 API 共用入口:
///      MongoDB 单条 FindOneAndUpdate 原子(条件 + $inc + $set),防并发同账号双扣超发(SV11);
///   3. SendDeltaPushTo — 写库成功后服务端起推送到该 UUID 在线全部会话(SV6/SV7,§3.3.3 + §5.4)。
///
/// 反作弊红线(SV17 Code Review 重点):
///   - 单条原子命令,**非**先 Find 后 Update 两步(plan §3.4 + §5.3 + §5.4 + §九风险表);
///   - 仅消费类型 + delta + reason 三入参,**不**接受绝对余额(§3.3.2 + §5.5,Code Review 必拦);
///   - 余额下界 + 类型上界条件**写在 FindOneAndUpdate 的过滤条件内**(MongoDB 端校验,
///     非应用层先比较后写);
///   - setOnInsert 仅在 insert 时写余额字段、update 路径完全不动余额(SV5);
///   - MongoDB 不可达 → 返 ServiceUnavailable 结果码,**不抛异常断连**(沿 30 / 35 基线)。
///
/// 设计基线:design-docs/37-player-attr-server.md §3.2 / §3.4 / §3.5。
/// </summary>
public static class PlayerPropertyServiceHelper
{
    /// <summary>
    /// 35 RegisterOrLogin 处理链调用:首登 setOnInsert 写初始 players 记录 + 读三属性当前余额。
    ///
    /// 单条原子 upsert:
    ///   filter: _id == accountId
    ///   update: $setOnInsert(_id / 三初始余额 / SchemaVersion / 首次 LastChangeUnixMs);
    ///           **不**对余额字段加 $set(沿 35 「首次注册时间只在 insert 时写」基线,SV5);
    ///   IsUpsert: true(不存在则 insert 首登 / 存在则 update,但本 update 无任何 $set 故无效写,
    ///             余额字段稳定 = 上次变更后的值);
    ///   ReturnDocument: After(取写后的当前文档,首登 = 初始值 / 重登 = 既有值)。
    ///
    /// 返回 (errorCode, doc)。errorCode 0 = 成功(doc = 玩家完整文档:三属性余额 + 昵称/等级/经验);
    /// 非 0 = MongoDB 不可达 / 异常 / 服务未挂(doc 为 null)。
    /// </summary>
    public static async FTask<(uint errorCode, PlayerDoc? doc)> InitOrLoad(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent 组件(应挂在 Gate Scene 上)。");
            return (1u, null);
        }

        var players = service.Players;
        if (players == null)
        {
            // MongoDB 不可达 — AwakeSystem 已 Warning;此处不重复 Warning。
            // 登录失败短路:不挂会话身份(plan §3.2 失败硬约束 + 35 同口径)。
            return (1u, null);
        }

        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);

        // $setOnInsert:首登写 _id + 三初始余额 + 档案初值(昵称空/等级1/经验0)+ LastChangeUnixMs + SchemaVersion;
        // update 路径(已存在)完全跳过(MongoDB 官方语义:$setOnInsert 仅在 upsert 触发 insert 时写)。
        // 全部走 $setOnInsert,**无 $set** → 重登 update 命令为空(MongoDB 允许空 update),
        // 字段稳定 = 上次变更后的值(SV5)。
        var update = Builders<PlayerDoc>.Update
            .SetOnInsert(x => x.AccountId, accountId)
            .SetOnInsert(x => x.Coin, service.CoinInitial)
            .SetOnInsert(x => x.Diamond, service.DiamondInitial)
            .SetOnInsert(x => x.Stamina, service.StaminaInitial)
            .SetOnInsert(x => x.Nickname, string.Empty)
            .SetOnInsert(x => x.Level, 1)
            .SetOnInsert(x => x.Exp, 0L)
            .SetOnInsert(x => x.LastChangeUnixMs, nowMs)
            .SetOnInsert(x => x.SchemaVersion, PlayerPropertyServiceComponent.CurrentSchemaVersion);

        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var doc = await players.FindOneAndUpdateAsync(filter, update, options);
            if (doc == null)
            {
                // 理论上 IsUpsert=true + ReturnDocument.After 不应返 null;防御性处理。
                Log.Warning($"PlayerPropertyServiceHelper.InitOrLoad: FindOneAndUpdate 返 null,accountId={accountId}。");
                return (1u, null);
            }

            return (0u, doc);
        }
        catch (MongoException e)
        {
            // 写入异常 / 网络抖动 / 集群挂:登录失败、不挂会话身份(沿 35 「服务不可用不本地放行」基线)。
            Log.Warning($"PlayerPropertyServiceHelper.InitOrLoad 失败,accountId={accountId},err={e.Message}");
            return (1u, null);
        }
    }

    /// <summary>
    /// 通用变更入口(PropertyChangeRequest handler / 进程内 API 共用,plan D7 + §3.5)。
    ///
    /// 校验顺序(§3.4 校验体系):
    ///   1. 服务就绪(组件 / players 句柄非空);
    ///   2. 类型枚举合法(三类之一);
    ///   3. delta 范围合法([-类型上界, +类型上界],防 long 极值绕过余额检查,§5.5);
    ///   4. MongoDB 单条原子 FindOneAndUpdate:
    ///      filter: _id == accountId AND 0 <= 当前余额 + delta <= 类型上界
    ///      update: $inc(该属性, delta) + $set(LastChangeUnixMs)
    ///      ReturnDocument: After(成功 → 取新余额回包);
    ///   5. 匹配失败 → 按 delta 正负返 NotEnough / OverLimit + 含当前实际余额(§3.4)。
    ///
    /// 返回 (resultCode, newAmount):
    ///   - Success:newAmount = 变更后新余额;
    ///   - NotEnough / OverLimit:newAmount = 当前实际余额(便于客户端段下一刀 toast「需要 X,你有 Y」);
    ///   - 其它失败:newAmount = 0。
    ///
    /// 仅 Success 时由调用方起推送(`SendDeltaPushTo`);本方法**不**直接起推送
    /// (调用方上下文不同:handler 在请求路径,进程内 API 在业务系统刀,推送时机由调用方控)。
    /// </summary>
    public static async FTask<(PropertyChangeResultCode resultCode, long newAmount)> ChangeProperty(
        Scene scene, string accountId, PropertyType type, long delta, string reason)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent 组件(应挂在 Gate Scene 上)。");
            return (PropertyChangeResultCode.ServiceUnavailable, 0L);
        }

        var players = service.Players;
        if (players == null)
        {
            return (PropertyChangeResultCode.ServiceUnavailable, 0L);
        }

        // 类型合法性:三类枚举之一(SV10)。
        if (!TryGetTypeMeta(service, type, out var fieldName, out var upperBound))
        {
            return (PropertyChangeResultCode.UnknownType, 0L);
        }

        // delta 范围合法性:[-上界, +上界],防 long.MaxValue 极值溢出绕过条件过滤(§3.4 + §5.5)。
        if (delta < -upperBound || delta > upperBound)
        {
            return (PropertyChangeResultCode.InvalidRequest, 0L);
        }

        var nowMs = TimeHelper.Now;
        // 用字段名构造过滤条件,统一三属性同套原子写。
        // 余额下界:当前余额 + delta >= 0,等价于 当前余额 >= -delta。
        // 类型上界:当前余额 + delta <= 上界,等价于 当前余额 <= 上界 - delta。
        // 两条件在 MongoDB FindOneAndUpdate 的 filter 中合并 AND,单条原子命令(SV11)。
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Gte(fieldName, -delta),
            Builders<PlayerDoc>.Filter.Lte(fieldName, upperBound - delta));

        var update = Builders<PlayerDoc>.Update
            .Inc(fieldName, delta)
            .Set(x => x.LastChangeUnixMs, nowMs);

        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = false,  // 变更前必须已经首登过(InitOrLoad 已在 35 登录链路写入);未首登 → 匹配失败 → 返 NotEnough(等价于「无账号 = 余额 0,扣 X = 余额不足」)。
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var doc = await players.FindOneAndUpdateAsync(filter, update, options);
            if (doc != null)
            {
                // 写入成功:取新余额(变更后)。
                var newAmount = GetFieldValue(doc, type);
                Log.Debug($"PlayerProperty 变更成功 account={accountId} type={type} delta={delta} reason='{reason}' newAmount={newAmount}");

                // 设计 44 §3.4:ledger 旁路追加挂在 FindOneAndUpdate 成功裁决后、本方法 return 之前。
                // 调用方 return 后才 SendDeltaPushTo → 推送给客户端,保证 ledger 永远先于推送写入。
                // ledger 失败仅告警不回滚(余额已成功定格,SV11);AttrLedger 句柄 null(MongoDB 不可达)静默跳过。
                // BalanceBefore = newAmount - delta(等价于 returnDocument Before,设计 44 §3.1)。
                await AttrLedgerHelper.AppendAsync(
                    service, accountId, type,
                    balanceBefore: newAmount - delta,
                    balanceAfter: newAmount,
                    delta: delta,
                    reasonRaw: reason ?? string.Empty,
                    timestampMs: nowMs);

                return (PropertyChangeResultCode.Success, newAmount);
            }

            // 匹配失败:可能是余额不足 / 上界溢出 / 账号未首登。
            // 单独查询当前余额,据 delta 正负判定 NotEnough vs OverLimit(便于客户端段下一刀 toast)。
            var currentDoc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId))
                .FirstOrDefaultAsync();
            if (currentDoc == null)
            {
                // 未首登 = 等价于零余额扣不动 / 加不进 → 返 ServiceUnavailable(理论上登录链路已保证首登,实战防御)。
                Log.Warning($"PlayerProperty 变更:账号 {accountId} 在 players 集合不存在(未首登?),按 ServiceUnavailable 返。");
                return (PropertyChangeResultCode.ServiceUnavailable, 0L);
            }

            var currentAmount = GetFieldValue(currentDoc, type);
            // delta < 0 = 扣减场景,匹配失败必是余额不足(因上界条件 currentAmount <= upperBound - delta
            // 在 delta<0 时变为 currentAmount <= upperBound + |delta|,几乎一定成立)。
            // delta > 0 = 增加场景,匹配失败必是上界溢出。
            // delta == 0:无意义变更,理论上必匹配成功(条件 0<=0 且 currentAmount<=upperBound 必满足)。
            var resultCode = delta < 0
                ? PropertyChangeResultCode.NotEnough
                : PropertyChangeResultCode.OverLimit;
            Log.Debug($"PlayerProperty 变更失败 account={accountId} type={type} delta={delta} reason='{reason}' result={resultCode} currentAmount={currentAmount}");
            return (resultCode, currentAmount);
        }
        catch (MongoException e)
        {
            // 写入异常(MongoDB 不可达 / 网络抖动 / 集群挂):返 ServiceUnavailable,不抛异常断连。
            Log.Warning($"PlayerPropertyServiceHelper.ChangeProperty 失败 account={accountId} type={type} delta={delta} reason='{reason}',err={e.Message}");
            return (PropertyChangeResultCode.ServiceUnavailable, 0L);
        }
    }

    /// <summary>
    /// 推送属性变更到该 UUID 在线全部会话(§3.3.3 + §5.4)。
    /// 当前 demo Account 内存态字典每 UUID 只持一个 Account / 一个会话(同 UUID 二次登录 Add 返 false,
    /// 见 AccountManageComponentSystem.Add),「全部会话」实际 = 单会话。
    /// 推送是绝对快照,丢失 = 下次登录拉快照对齐(O6 不重试)。
    /// 离线 = 推送丢弃(找不到 Account / Session 已断,不报错)。
    /// </summary>
    public static void SendDeltaPushTo(Scene scene, string accountId, PropertyType type, long newAmount, string reason)
    {
        if (!AccountManageHelper.TryGetAccount(scene, accountId, out var account))
        {
            // 离线 → 推送丢弃(§5.6/§5.7,下次登录的 InitOrLoad 快照会带最新值)。
            return;
        }

        Session session = account.Session;
        if (session == null || session.IsDisposed)
        {
            return;
        }

        // 与 G2C_PushMessage 推送同范式:session.Send(IMessage) 一行,框架自动序列化 + opcode。
        // 不走 RPC 等待响应(推送是单向);失败 = 不重试(O6,Tier 3 强一致时再加跨进程层)。
        session.Send(new G2C_PropertyDeltaPush
        {
            Type = type,
            NewAmount = newAmount,
            Reason = reason ?? string.Empty
        });
    }

    /// <summary>
    /// 推送玩家信息整份快照到指定会话(登录成功后即时下发)。
    /// 取代原 G2C_PropertyInitSnapshot:一条 G2C_PlayerInfoSnapshot 同时携带基础档案(昵称/等级/经验)与三数值属性余额。
    /// 走指定 session(刚登录的连接),不查 Account 字典——InitOrLoad 已在 Login Handler 取回 doc,调用方拿到 Session 即可直推。
    /// </summary>
    public static void SendPlayerInfoTo(Session session, string accountId, PlayerDoc? doc)
    {
        if (session == null || session.IsDisposed || doc == null)
        {
            return;
        }

        var info = PlayerInfo.Create();
        info.AccountId = accountId;
        info.Nickname = doc.Nickname;
        info.Level = doc.Level;
        info.Exp = doc.Exp;
        info.SchemaVersion = PlayerPropertyServiceComponent.CurrentSchemaVersion;
        AddProperty(info, PropertyType.Coin, doc.Coin);
        AddProperty(info, PropertyType.Diamond, doc.Diamond);
        AddProperty(info, PropertyType.Stamina, doc.Stamina);

        session.Send(new G2C_PlayerInfoSnapshot { Info = info });
    }

    /// <summary>把单条属性余额追加到 PlayerInfo.Properties(复用 PropertyAmount 对象池)。</summary>
    private static void AddProperty(PlayerInfo info, PropertyType type, long amount)
    {
        var item = PropertyAmount.Create();
        item.Type = type;
        item.Amount = amount;
        info.Properties.Add(item);
    }

    /// <summary>
    /// 取出某类型对应的 PlayerDoc BSON 字段名 + 配置上界。
    /// 字段名按 BSON 序列化默认 = C# 属性名(无 [BsonElement] 重命名,PlayerDoc 没用)。
    /// 未知类型返 false → 触发 UnknownType 结果码(§3.4 + SV10)。
    /// </summary>
    private static bool TryGetTypeMeta(PlayerPropertyServiceComponent service, PropertyType type, out string fieldName, out long upperBound)
    {
        switch (type)
        {
            case PropertyType.Coin:
                fieldName = nameof(PlayerDoc.Coin);
                upperBound = service.CoinUpperBound;
                return true;
            case PropertyType.Diamond:
                fieldName = nameof(PlayerDoc.Diamond);
                upperBound = service.DiamondUpperBound;
                return true;
            case PropertyType.Stamina:
                fieldName = nameof(PlayerDoc.Stamina);
                upperBound = service.StaminaUpperBound;
                return true;
            default:
                fieldName = string.Empty;
                upperBound = 0L;
                return false;
        }
    }

    /// <summary>取出 PlayerDoc 中某类型对应的余额字段值。</summary>
    private static long GetFieldValue(PlayerDoc doc, PropertyType type)
    {
        return type switch
        {
            PropertyType.Coin => doc.Coin,
            PropertyType.Diamond => doc.Diamond,
            PropertyType.Stamina => doc.Stamina,
            _ => 0L
        };
    }
}
