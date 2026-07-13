using System;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using MongoDB.Bson;
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
    /// 返回 (errorCode, message, doc)。errorCode 0 = 成功(message 空串,doc = 玩家完整文档:三属性余额 + 昵称/等级/经验);
    /// 非 0 = MongoDB 不可达 / 异常 / 服务未挂(message 为面向排障的中文原因,doc 为 null)。
    /// </summary>
    public static async FTask<(uint errorCode, string message, PlayerDoc? doc)> InitOrLoad(
        Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent 组件(应挂在 Gate Scene 上)。");
            return (1u, "玩家数据初始化失败:数据服务不可用", null);
        }

        var players = service.Players;
        if (players == null)
        {
            // MongoDB 不可达 — AwakeSystem 已 Warning;此处不重复 Warning。
            // 登录失败短路:不挂会话身份(plan §3.2 失败硬约束 + 35 同口径)。
            return (1u, "玩家数据初始化失败:数据服务不可用", null);
        }

        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);

        // $setOnInsert:首登写 _id + 三初始余额 + 档案初值(昵称空/等级1/经验0)+ LastChangeUnixMs + SchemaVersion;
        // update 路径(已存在)完全跳过(MongoDB 官方语义:$setOnInsert 仅在 upsert 触发 insert 时写)。
        // 全部走 $setOnInsert,**无 $set** → 重登 update 命令为空(MongoDB 允许空 update),
        // 字段稳定 = 上次变更后的值(SV5)。
        var update = Builders<PlayerDoc>.Update
            .SetOnInsert(x => x.AccountId, accountId)
            // PlayerId 首登写空串(present-and-empty),与其他字段一致;
            // 不写则文档无该字段,后续 ClaimOrIssuePlayerId 的「PlayerId == ""」filter 永不匹配。
            // 配合 ux_player_id partial index 用 $gt:"" 过滤,空串不进入唯一约束。
            .SetOnInsert(x => x.PlayerId, string.Empty)
            .SetOnInsert(x => x.Diamond, service.DiamondInitial)
            // P2 新增四货币首登 setOnInsert(旧文档反序列化时缺字段 → BSON 默认 0L,与 setOnInsert 0 一致;
            // 但 EnergyLastRecoverMs 必须 setOnInsert 为 nowMs,否则首登玩家流逝时间 = nowMs - 0 = 极大值,体力会一次性回满)。
            .SetOnInsert(x => x.SoulPower, service.SoulPowerInitial)
            .SetOnInsert(x => x.Piety, service.PietyInitial)
            .SetOnInsert(x => x.GuardianExp, service.GuardianExpInitial)
            .SetOnInsert(x => x.Energy, service.EnergyInitial)
            .SetOnInsert(x => x.EnergyLastRecoverMs, nowMs)
            // P2 Phase 1·订单进度状态显式写默认值(数值字段缺失 BSON 反序列化为 0 与本处 setOnInsert 0 一致;
            // 但显式 setOnInsert 让首登 doc 字段全部 present,后续 CAS filter Eq(0L) / Eq(0) 形态稳定不依赖 absent==0 隐式语义)。
            .SetOnInsert(x => x.OrderCursor, 0)
            .SetOnInsert(x => x.LastOrderRefreshMs, 0L)
            .SetOnInsert(x => x.OrderDeliveredMask, 0)
            // P3 五元层进度计数器首登 setOnInsert(全新玩家进度为 0;显式写使字段 present)。
            .SetOnInsert(x => x.GoddessRating, service.GoddessRatingInitial)
            .SetOnInsert(x => x.UnlockedChapter, service.UnlockedChapterInitial)
            .SetOnInsert(x => x.BlindBoxCount, service.BlindBoxCountInitial)
            .SetOnInsert(x => x.TempleRepaired, service.TempleRepairedInitial)
            .SetOnInsert(x => x.NextRepairIndex, service.NextRepairIndexInitial)
            // 头像 / 框服务端权威(2c):当前 id 缺省与客户端默认对齐;解锁集合缺省空(客户端 bootstrap 上报默认解锁)。
            .SetOnInsert(x => x.CurrentAvatarId, service.CurrentAvatarIdInitial)
            .SetOnInsert(x => x.CurrentFrameId, service.CurrentFrameIdInitial)
            .SetOnInsert(x => x.UnlockedAvatarIds, new System.Collections.Generic.List<int>())
            .SetOnInsert(x => x.UnlockedFrameIds, new System.Collections.Generic.List<int>())
            // 祈愿服务端权威(3a):今日次数首登 0;上次重置时刻 setOnInsert=nowMs(同 EnergyLastRecoverMs 手法,
            //   避免 0L 被 TransitionLocal 判为 1970 年、首登即误判跨天)。
            .SetOnInsert(x => x.WishUsedToday, 0)
            .SetOnInsert(x => x.WishLastResetUnixMs, nowMs)
            // 看广告领体力每日闸服务端权威:今日次数首登 0;上次重置时刻 setOnInsert=nowMs(同祈愿手法,避免 0L 被 TransitionLocal 判 1970 年、首登即误判跨天)。
            .SetOnInsert(x => x.AdEnergyUsedToday, 0)
            .SetOnInsert(x => x.AdEnergyLastResetUnixMs, nowMs)
            // 钻石购买体力每日闸服务端权威(Round F):今日次数首登 0;上次重置时刻 setOnInsert=nowMs(同看广告手法,避免 0L 被 TransitionLocal 判 1970 年、首登即误判跨天)。
            .SetOnInsert(x => x.BuyEnergyUsedToday, 0)
            .SetOnInsert(x => x.BuyEnergyLastResetUnixMs, nowMs)
            // 皮肤态 / 神庙装饰标志服务端权威(3b):缺省对齐客户端默认(彩色 0 / 单色 id -1=Unselected / 无装饰 0)。
            .SetOnInsert(x => x.SkinMono, 0)
            .SetOnInsert(x => x.SkinMonoId, -1)
            .SetOnInsert(x => x.TempleDecorated, 0L)
            // 道具持有 / 塔罗进度:首登空字典(显式写使字段 present,购买 CAS 的 filter 形态稳定)。
            .SetOnInsert(x => x.ItemHoldings, new System.Collections.Generic.Dictionary<string, long>())
            .SetOnInsert(x => x.TarotProgress, new System.Collections.Generic.Dictionary<string, int>())
            // 背包批次轨 / 使用幂等锚 / 批次数组乐观版本:首登空数组 / 0 / 0(显式写使字段 present)。
            .SetOnInsert(x => x.ItemLots, new System.Collections.Generic.List<ItemLot>())
            .SetOnInsert(x => x.LastUseReqSeq, 0L)
            .SetOnInsert(x => x.InventoryVersion, 0L)
            .SetOnInsert(x => x.Nickname, string.Empty)
            .SetOnInsert(x => x.Level, 1)
            .SetOnInsert(x => x.Exp, 0L)
            .SetOnInsert(x => x.RenameCount, 0)
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
                return (1u, "玩家数据初始化失败:数据服务不可用", null);
            }

            // 旧 schema 文档补字段:setOnInsert 仅 insert 触发,重登 update 路径完全不写,
            // 故旧档(本次 CurrentSchemaVersion 之前注册的玩家)文档里 P1/P2 新增字段(四货币 / 体力 / 订单进度)
            // 全部 absent。MongoDB 中「字段缺失」≠「默认值」:absent 字段对 Eq(field,0) / $bitsAllClear 等 filter 永不匹配,
            // 直接让订单交付 CAS、体力恢复 bootstrap 等依赖「字段 present」的原子裁决全部空命中。
            // 登录处理链先于一切玩法读写,在此原子补齐到 CurrentSchemaVersion(present 但保留既有值),恢复后续裁决前提。
            doc = await MigrateSchemaIfNeeded(service, accountId, doc, nowMs);

            // P2 体力恢复:登录拉快照前结算一次,保证客户端拿到的 Energy 是「补完恢复」的最新值。
            // 经上面补字段后 EnergyLastRecoverMs 必 present(旧档补为 nowMs),恢复结算口径稳定。
            await RecoverEnergyIfDue(service, accountId, doc, nowMs);

            // 祈愿每日重置(3a):登录拉快照前跑一次懒重置,保证客户端登录看到的 WishUsedToday 是「重置后」的当日值。
            // 经上面补字段后 WishLastResetUnixMs 必 present(旧档补为 nowMs),跨天判据口径稳定。
            await WishHelper.ResetWishIfDue(service, accountId, doc, nowMs);

            // 看广告领体力每日重置:登录拉快照前跑一次懒重置,保证客户端登录看到的 AdEnergyUsedToday 是「重置后」的当日值。
            // 经上面补字段后 AdEnergyLastResetUnixMs 必 present(旧档补为 nowMs),跨天判据口径稳定。
            await ClaimAdEnergyHelper.ResetAdEnergyIfDue(service, accountId, doc, nowMs);

            // 钻石购买体力每日重置(Round F):登录拉快照前跑一次懒重置,保证客户端登录看到的 BuyEnergyUsedToday 是「重置后」当日值。
            // 经上面补字段后 BuyEnergyLastResetUnixMs 必 present(旧档补为 nowMs),跨天判据口径稳定。
            await BuyEnergyHelper.ResetBuyEnergyIfDue(service, accountId, doc, nowMs);

            // 背包惰性过期结算:登录拉快照前剔除已过期批次(无补偿删库 + 记流水,有补偿留库待结算),
            // 就地更新 doc.ItemLots,使随后快照下发的批次轨不含已过期批次。
            await InventoryServiceHelper.SettleExpiredLotsAtLogin(service, accountId, doc, nowMs);

            return (0u, string.Empty, doc);
        }
        catch (MongoException e)
        {
            // 写入异常 / 网络抖动 / 集群挂:登录失败、不挂会话身份(沿 35 「服务不可用不本地放行」基线)。
            Log.Warning($"PlayerPropertyServiceHelper.InitOrLoad 失败,accountId={accountId},err={e.Message}");
            return (1u, "玩家数据初始化失败:数据服务不可用", null);
        }
    }

    /// <summary>
    /// 旧 schema 文档原子补字段:把 SchemaVersion 落后于 CurrentSchemaVersion 的旧档补齐到当前版本。
    ///
    /// 背景:InitOrLoad 用 $setOnInsert 写初值,仅在文档 insert 时触发;此后每次重登走 update 路径完全不写新字段。
    /// 所以 CurrentSchemaVersion 之前注册的玩家文档里,后加的字段(P1/P2 的四货币 / 体力恢复 / 订单进度)全部 absent。
    /// 「字段缺失(absent)」≠「字段 = 默认值」:absent 字段对 Eq(field,0) / Eq(field,0L) / $bitsAllClear 等 filter 永不命中,
    /// 直接令订单交付 CAS(MergeOrderServiceHelper.TryDeliver claim)与体力恢复 bootstrap 等依赖「字段 present」的原子裁决空命中。
    ///
    /// 修法:用聚合管线 update + $ifNull 把每个字段 set 为「present 则保留既有值,absent 则填默认值」,
    /// 单条原子 FindOneAndUpdate 完成,既不覆盖玩家既有数据(Diamond/PlayerId 等已存在的值原样保留),
    /// 又保证补齐后所有字段 present、SchemaVersion 升到当前。filter 含 SchemaVersion < CurrentSchemaVersion → 幂等:
    /// 已是当前版本的文档不命中、不重复执行(并发多登录也只一路补成,其余路重读拿到已补齐文档)。
    ///
    /// 默认值口径与 InitOrLoad 的 $setOnInsert 完全一致(同一组 service.*Initial 运营配置 + EnergyLastRecoverMs=nowMs):
    /// 旧玩家本就没有这些新货币 / 订单进度,补为初值即「等价于该字段从一开始就 present 为默认值」,语义正确。
    ///
    /// 失败 / 命中 null(已是当前版本,或并发被另一路补完)→ 返回入参 doc 的最新可用视图(命中则用补后文档),不抛、不阻断登录。
    /// </summary>
    private static async FTask<PlayerDoc> MigrateSchemaIfNeeded(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        if (doc.SchemaVersion >= PlayerPropertyServiceComponent.CurrentSchemaVersion)
        {
            return doc;
        }

        var players = service.Players;
        if (players == null) return doc;

        // 聚合管线 $set + $ifNull:字段 present 保留既有值,absent 填默认值。默认值口径对齐 InitOrLoad setOnInsert。
        var setStage = new BsonDocument
        {
            { "Diamond",             new BsonDocument("$ifNull", new BsonArray { "$Diamond", service.DiamondInitial }) },
            { "SoulPower",           new BsonDocument("$ifNull", new BsonArray { "$SoulPower", service.SoulPowerInitial }) },
            { "Piety",               new BsonDocument("$ifNull", new BsonArray { "$Piety", service.PietyInitial }) },
            { "GuardianExp",         new BsonDocument("$ifNull", new BsonArray { "$GuardianExp", service.GuardianExpInitial }) },
            { "Energy",              new BsonDocument("$ifNull", new BsonArray { "$Energy", service.EnergyInitial }) },
            { "EnergyLastRecoverMs", new BsonDocument("$ifNull", new BsonArray { "$EnergyLastRecoverMs", nowMs }) },
            // 订单进度三字段:v10 起**无条件重置**(非 $ifNull 保留)。订单池由代码 8 条迁 Luban TbMergeOrder 22 条,
            // 游标派生 pool[(cursor+i) mod 池长] 的取模基数变了,旧档 cursor/mask 指向的订单整体漂移、标记失义;
            // 重置三字段 = 等价重新发第一批(LastOrderRefreshMs=0 → 下次接触 bootstrap 重锚定),一次性、幂等
            // (migrateFilter 含 SchemaVersion < 当前版,已迁档不再命中)。
            { "OrderCursor",         0 },
            { "LastOrderRefreshMs",  0L },
            { "OrderDeliveredMask",  0 },
            // P3 五元层进度计数器:旧档(schema < 5)缺字段 → 补 Initial(默认 0),present 则保留既有值。
            { "GoddessRating",       new BsonDocument("$ifNull", new BsonArray { "$GoddessRating", service.GoddessRatingInitial }) },
            { "UnlockedChapter",     new BsonDocument("$ifNull", new BsonArray { "$UnlockedChapter", service.UnlockedChapterInitial }) },
            { "BlindBoxCount",       new BsonDocument("$ifNull", new BsonArray { "$BlindBoxCount", service.BlindBoxCountInitial }) },
            { "TempleRepaired",      new BsonDocument("$ifNull", new BsonArray { "$TempleRepaired", service.TempleRepairedInitial }) },
            { "NextRepairIndex",     new BsonDocument("$ifNull", new BsonArray { "$NextRepairIndex", service.NextRepairIndexInitial }) },
            { "PlayerId",            new BsonDocument("$ifNull", new BsonArray { "$PlayerId", string.Empty }) },
            { "Nickname",            new BsonDocument("$ifNull", new BsonArray { "$Nickname", string.Empty }) },
            { "Level",               new BsonDocument("$ifNull", new BsonArray { "$Level", 1 }) },
            { "Exp",                 new BsonDocument("$ifNull", new BsonArray { "$Exp", 0L }) },
            // 改名服务端权威:旧档(schema < 6)缺 RenameCount → 补 0(等价该玩家从未改名),present 则保留既有值。
            { "RenameCount",         new BsonDocument("$ifNull", new BsonArray { "$RenameCount", 0 }) },
            // 头像 / 框服务端权威(2c):旧档(schema < 7)缺当前 id → 补客户端默认(头像 1 / 框 101),present 则保留;
            //   缺解锁集合 → 补空数组(客户端 bootstrap 上报默认解锁),present 则保留既有集合。
            { "CurrentAvatarId",     new BsonDocument("$ifNull", new BsonArray { "$CurrentAvatarId", service.CurrentAvatarIdInitial }) },
            { "CurrentFrameId",      new BsonDocument("$ifNull", new BsonArray { "$CurrentFrameId", service.CurrentFrameIdInitial }) },
            { "UnlockedAvatarIds",   new BsonDocument("$ifNull", new BsonArray { "$UnlockedAvatarIds", new BsonArray() }) },
            { "UnlockedFrameIds",    new BsonDocument("$ifNull", new BsonArray { "$UnlockedFrameIds", new BsonArray() }) },
            // 祈愿服务端权威(3a):旧档(schema < 8)缺祈愿字段 → 次数补 0、上次重置时刻补 nowMs
            //   (补 nowMs 而非 0,避免旧档补齐当次即被 TransitionLocal 判 1970 年跨天;present 则保留既有值)。
            { "WishUsedToday",       new BsonDocument("$ifNull", new BsonArray { "$WishUsedToday", 0 }) },
            { "WishLastResetUnixMs", new BsonDocument("$ifNull", new BsonArray { "$WishLastResetUnixMs", nowMs }) },
            // 看广告领体力每日闸(schema < 13):旧档缺字段 → 次数补 0、上次重置时刻补 nowMs(present 则保留既有值)。
            { "AdEnergyUsedToday",       new BsonDocument("$ifNull", new BsonArray { "$AdEnergyUsedToday", 0 }) },
            { "AdEnergyLastResetUnixMs", new BsonDocument("$ifNull", new BsonArray { "$AdEnergyLastResetUnixMs", nowMs }) },
            // 钻石购买体力每日闸(schema < 14):旧档缺字段 → 次数补 0、上次重置时刻补 nowMs(present 则保留既有值)。
            { "BuyEnergyUsedToday",       new BsonDocument("$ifNull", new BsonArray { "$BuyEnergyUsedToday", 0 }) },
            { "BuyEnergyLastResetUnixMs", new BsonDocument("$ifNull", new BsonArray { "$BuyEnergyLastResetUnixMs", nowMs }) },
            // 皮肤态 / 神庙装饰标志服务端权威(3b):旧档(schema < 9)缺字段 → 补客户端默认(彩色 0 / 单色 id -1 / 无装饰 0),present 则保留既有值。
            { "SkinMono",            new BsonDocument("$ifNull", new BsonArray { "$SkinMono", 0 }) },
            { "SkinMonoId",          new BsonDocument("$ifNull", new BsonArray { "$SkinMonoId", -1 }) },
            { "TempleDecorated",     new BsonDocument("$ifNull", new BsonArray { "$TempleDecorated", 0L }) },
            // 道具持有:旧档缺字段 → 补空字典(absent 字段对 CAS filter 永不命中,补齐后形态稳定),present 则保留既有值。
            { "ItemHoldings",        new BsonDocument("$ifNull", new BsonArray { "$ItemHoldings", new BsonDocument() }) },
            // 塔罗进度(schema 12 起):旧档缺字段 → 补空字典,present 则保留既有值。塔罗玩法由碎片合成改为虔诚币购买进度,
            //   旧 CollectedTarotIds 字段随之退役(不再映射到 PlayerDoc,由 IgnoreExtraElements 约定忽略残留)。
            //   该收集功能改版前无 UI 消费、无正式玩家数据,故进度从空重建而非从旧收集集播种。
            { "TarotProgress",       new BsonDocument("$ifNull", new BsonArray { "$TarotProgress", new BsonDocument() }) },
            // 背包批次轨 / 使用幂等锚 / 批次数组乐观版本:旧档(schema < 11)缺字段 → 补空数组 / 0 / 0,present 则保留既有值。
            { "ItemLots",            new BsonDocument("$ifNull", new BsonArray { "$ItemLots", new BsonArray() }) },
            { "LastUseReqSeq",       new BsonDocument("$ifNull", new BsonArray { "$LastUseReqSeq", 0L }) },
            { "InventoryVersion",    new BsonDocument("$ifNull", new BsonArray { "$InventoryVersion", 0L }) },
            { "LastChangeUnixMs",    new BsonDocument("$ifNull", new BsonArray { "$LastChangeUnixMs", nowMs }) },
            { "SchemaVersion",       PlayerPropertyServiceComponent.CurrentSchemaVersion },
        };
        var pipeline = new BsonDocument[] { new BsonDocument("$set", setStage) };

        var migrateFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Lt(x => x.SchemaVersion, PlayerPropertyServiceComponent.CurrentSchemaVersion));

        try
        {
            var migrated = await players.FindOneAndUpdateAsync<PlayerDoc>(
                migrateFilter,
                Builders<PlayerDoc>.Update.Pipeline(pipeline),
                new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
            if (migrated != null)
            {
                Log.Info($"PlayerPropertyServiceHelper.MigrateSchemaIfNeeded 补字段成功 account={accountId} fromVersion={doc.SchemaVersion} toVersion={PlayerPropertyServiceComponent.CurrentSchemaVersion}");
                return migrated;
            }
            // null:并发已被另一路补完(或刚好被改成 ≥ 当前版本)→ 重读拿最新文档;读失败兜底返入参 doc。
            var fresh = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            return fresh ?? doc;
        }
        catch (MongoException e)
        {
            // 补字段失败不阻断登录:返入参 doc(后续依赖 present 字段的玩法仍可能空命中,但登录链路不崩;下次登录重试补)。
            Log.Warning($"PlayerPropertyServiceHelper.MigrateSchemaIfNeeded 失败 account={accountId},err={e.Message}");
            return doc;
        }
    }

    /// <summary>
    /// 清档·把玩家文档重置为默认新手态(保留账号身份:AccountId 与 PlayerId 不动)。
    ///
    /// 字段集与 InitOrLoad 的 setOnInsert 完全一致(同一组 service.*Initial 运营配置值 + 同样的
    /// EnergyLastRecoverMs=nowMs / 订单游标清零 / Nickname 空 / Level=1 / Exp=0 / SchemaVersion=当前版本),
    /// 故清后重登拿到的快照与全新注册玩家逐字段一致。区别仅在于:
    ///   - 用单条原子 $set 把所有玩法字段覆盖为默认值(非 $setOnInsert,因为文档已存在);
    ///   - **不触碰** AccountId(主键)与 PlayerId(账号级稳定身份锚),保证账号→playerId 绑定不变。
    ///
    /// 原子性:单条 FindOneAndUpdate($set),整份重置一次性落库,不存在「清一半」的中间态。
    /// 幂等:重复清同一账号同样把字段刷成默认值,结果不变、不报错。
    /// IsUpsert=false:账号未首登(理论上清档前必已登录)→ 匹配失败、doc 为 null,返成功(无数据可清等价已是默认态)。
    ///
    /// 返回 errorCode:0 = 成功(含 doc 不存在的幂等成功);非 0 = MongoDB 不可达 / 异常。
    /// </summary>
    public static async FTask<uint> ResetToNewbie(Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent 组件(应挂在 Gate Scene 上)。");
            return 1u;
        }

        var players = service.Players;
        if (players == null)
        {
            return 1u;
        }

        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);

        // $set 所有玩法字段为默认值(对齐 InitOrLoad 的 setOnInsert);AccountId 是 _id 主键不可改、
        // PlayerId 故意不入 update 保留账号级身份锚。
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.Diamond, service.DiamondInitial)
            .Set(x => x.SoulPower, service.SoulPowerInitial)
            .Set(x => x.Piety, service.PietyInitial)
            .Set(x => x.GuardianExp, service.GuardianExpInitial)
            .Set(x => x.Energy, service.EnergyInitial)
            .Set(x => x.EnergyLastRecoverMs, nowMs)
            .Set(x => x.OrderCursor, 0)
            .Set(x => x.LastOrderRefreshMs, 0L)
            .Set(x => x.OrderDeliveredMask, 0)
            // P3 五元层进度计数器:清档重置为默认值(对齐 InitOrLoad setOnInsert)。
            .Set(x => x.GoddessRating, service.GoddessRatingInitial)
            .Set(x => x.UnlockedChapter, service.UnlockedChapterInitial)
            .Set(x => x.BlindBoxCount, service.BlindBoxCountInitial)
            .Set(x => x.TempleRepaired, service.TempleRepairedInitial)
            .Set(x => x.NextRepairIndex, service.NextRepairIndexInitial)
            // 头像 / 框服务端权威(2c):清档重置当前 id 为默认、解锁集合清空(对齐首登 setOnInsert)。
            .Set(x => x.CurrentAvatarId, service.CurrentAvatarIdInitial)
            .Set(x => x.CurrentFrameId, service.CurrentFrameIdInitial)
            .Set(x => x.UnlockedAvatarIds, new System.Collections.Generic.List<int>())
            .Set(x => x.UnlockedFrameIds, new System.Collections.Generic.List<int>())
            // 祈愿服务端权威(3a):清档重置今日次数为 0、上次重置时刻为 nowMs(对齐首登 setOnInsert)。
            .Set(x => x.WishUsedToday, 0)
            .Set(x => x.WishLastResetUnixMs, nowMs)
            // 看广告领体力每日闸:清档重置今日次数为 0、上次重置时刻为 nowMs(对齐首登 setOnInsert)。
            .Set(x => x.AdEnergyUsedToday, 0)
            .Set(x => x.AdEnergyLastResetUnixMs, nowMs)
            // 钻石购买体力每日闸:清档重置今日次数为 0、上次重置时刻为 nowMs(对齐首登 setOnInsert)。
            .Set(x => x.BuyEnergyUsedToday, 0)
            .Set(x => x.BuyEnergyLastResetUnixMs, nowMs)
            // 皮肤态 / 神庙装饰标志服务端权威(3b):清档重置为客户端默认(彩色 0 / 单色 id -1 / 无装饰 0)。
            .Set(x => x.SkinMono, 0)
            .Set(x => x.SkinMonoId, -1)
            .Set(x => x.TempleDecorated, 0L)
            // 道具持有 / 塔罗进度:清档清空(对齐首登 setOnInsert)。
            .Set(x => x.ItemHoldings, new System.Collections.Generic.Dictionary<string, long>())
            .Set(x => x.TarotProgress, new System.Collections.Generic.Dictionary<string, int>())
            // 背包批次轨 / 使用幂等锚 / 批次版本:清档清空批次、幂等锚与版本归 0(对齐首登 setOnInsert)。
            .Set(x => x.ItemLots, new System.Collections.Generic.List<ItemLot>())
            .Set(x => x.LastUseReqSeq, 0L)
            .Set(x => x.InventoryVersion, 0L)
            .Set(x => x.Nickname, string.Empty)
            .Set(x => x.Level, 1)
            .Set(x => x.Exp, 0L)
            .Set(x => x.RenameCount, 0)
            .Set(x => x.LastChangeUnixMs, nowMs)
            .Set(x => x.SchemaVersion, PlayerPropertyServiceComponent.CurrentSchemaVersion);

        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var doc = await players.FindOneAndUpdateAsync(filter, update, options);
            if (doc == null)
            {
                // 文档不存在 = 未首登 / 已被清空:无数据可清,等价已是默认态,幂等成功。
                Log.Debug($"PlayerPropertyServiceHelper.ResetToNewbie: 玩家文档不存在,视为已默认态,account={accountId}。");
                return 0u;
            }
            Log.Debug($"PlayerProperty 清档重置成功 account={accountId} playerId={doc.PlayerId}");
            return 0u;
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceHelper.ResetToNewbie 失败 account={accountId},err={e.Message}");
            return 1u;
        }
    }

    /// <summary>
    /// 签发或认领 playerId,确保返回非空权威值(账号级稳定唯一标识,跨登录不变)。
    ///
    /// 三种入参组合(localPlayerId 经格式校验 + 全局唯一性守卫后规整):
    ///   1. doc.PlayerId 非空 → 已有权威值,直接返回;若 localPlayerId 非空且与之不同,记 warning(便于排查老档残留 / 多端冲突)。
    ///   2. doc.PlayerId 空 + localPlayerId 合法且未被他人占用 → 认领老档本地 guid(单条原子 update)。
    ///   3. doc.PlayerId 空 + localPlayerId 空 / 畸形 / 已被他人占用 → 服务端 Guid.NewGuid() 新生成。
    ///
    /// 安全守卫(playerId 是 P1 排行榜归属 / P3 云存档寻址的身份锚,不可碰撞,不可伪造):
    ///   - 格式校验:localPlayerId 必须是 32 位小写 hex(`Guid.TryParseExact(..., "N", out _)`,
    ///     与客户端 PlayerInfo.NewId 形态对齐)。畸形 → 视同空值走服务端生成(Warning)。
    ///   - 全局唯一性守卫:认领前查全库是否已有**另一个账号**(AccountId != 当前)持有该 PlayerId;
    ///     占用 → 拒绝认领、改服务端生成(Warning),防止跨账号身份冒领。
    ///   - TOCTOU 收口:DB 端 players.PlayerId 部分唯一索引(ux_player_id, PartialFilter PlayerId > "")
    ///     兜底——查重 → 写入间隙被抢占时,FindOneAndUpdate 触发 E11000(MongoWriteException),
    ///     退回服务端 Guid.NewGuid() 重试。索引在 PlayerPropertyServiceComponentSystem.Init 建。
    ///
    /// 原子写规约:
    ///   filter: _id == accountId AND (PlayerId == "" OR PlayerId 字段缺失)
    ///   update: $set(PlayerId = 目标值)
    ///   ReturnDocument: After
    ///
    /// 并发场景:同账号两连接首登并发,两路径都拿到空 doc 各自要写;先到的 update 命中、后到的 filter 不命中
    /// (PlayerId 已被前者填写),后到回退去读当前 doc 拿到权威值——所以匹配失败必须重读一次。
    ///
    /// 返回 (errorCode, message, playerId)。0 = 成功(message 空串);
    /// 非 0 = MongoDB 异常或 doc 丢失(message 为面向排障的中文原因,playerId 空)。
    /// </summary>
    public static async FTask<(uint errorCode, string message, string playerId)> ClaimOrIssuePlayerId(
        Scene scene, string accountId, PlayerDoc? doc, string localPlayerId)
    {
        if (doc == null)
        {
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }

        // Case 1: 已有权威 PlayerId,直接返回(服务端为准,忽略客户端上传值,仅记不一致告警)。
        if (!string.IsNullOrEmpty(doc.PlayerId))
        {
            if (!string.IsNullOrEmpty(localPlayerId) && localPlayerId != doc.PlayerId)
            {
                Log.Warning($"PlayerId 不一致 account={accountId} server={doc.PlayerId} client={localPlayerId} → 以服务端为准(忽略客户端上传值)。");
            }
            return (0u, string.Empty, doc.PlayerId);
        }

        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null || service.Players == null)
        {
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }

        // 格式校验:客户端身份 id 形态 = 32 位小写 hex(Guid "N",见客户端 PlayerInfo.NewId)。
        // 畸形 / 超长 → 视同空值走服务端生成(防止畸形值被采纳进库)。
        var claimCandidate = localPlayerId ?? string.Empty;
        if (claimCandidate.Length > 0 && !Guid.TryParseExact(claimCandidate, "N", out _))
        {
            Log.Warning($"PlayerId 客户端上传值格式非法 account={accountId} localPlayerId='{claimCandidate}' → 改服务端生成。");
            claimCandidate = string.Empty;
        }

        // 全局唯一性守卫:认领前查全库是否已有**另一个账号**持有该 PlayerId;
        // 占用 → 拒绝认领、改服务端生成。这是 fast path,DB 端 partial unique index 兜底 TOCTOU。
        if (claimCandidate.Length > 0)
        {
            var ownerFilter = Builders<PlayerDoc>.Filter.And(
                Builders<PlayerDoc>.Filter.Eq(x => x.PlayerId, claimCandidate),
                Builders<PlayerDoc>.Filter.Ne(x => x.AccountId, accountId));
            try
            {
                var occupiedByOther = await service.Players.Find(ownerFilter).FirstOrDefaultAsync();
                if (occupiedByOther != null)
                {
                    Log.Warning($"PlayerId 客户端上传值已被他账号占用 account={accountId} localPlayerId={claimCandidate} occupiedBy={occupiedByOther.AccountId} → 改服务端生成。");
                    claimCandidate = string.Empty;
                }
            }
            catch (MongoException e)
            {
                Log.Warning($"PlayerPropertyServiceHelper.ClaimOrIssuePlayerId 唯一性查重失败 account={accountId},err={e.Message}");
                return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
            }
        }

        // 目标 playerId:认领候选合法且未被占用 → 用之;否则服务端 NewGuid 生成(走客户端 "N" 形态对齐)。
        var targetPlayerId = claimCandidate.Length > 0 ? claimCandidate : Guid.NewGuid().ToString("N");
        var sourceTag = claimCandidate.Length > 0 ? "client-claim" : "server-issue";

        // 原子 update:filter 含「PlayerId 尚未实占」(空串或字段缺失),防并发覆盖已被前者填写的值。
        // MongoDB 里「字段缺失(absent)」≠「空串」:旧档 / 未走 setOnInsert 写入 PlayerId 的新档,文档无该字段,
        // 单用 Eq("") 永不匹配 → 走回读分支拿到空 PlayerId → 登录 ErrorCode=1。
        // 用 Or(Eq(""), Exists(false)) 覆盖两种「未实占」形态,保持「仅在 PlayerId 还没被填」的原子认领语义。
        // DuplicateKey 退回分支(FallbackIssuePlayerId)复用本 filter,改这一处定义即两路一致。
        var notClaimed = Builders<PlayerDoc>.Filter.Or(
            Builders<PlayerDoc>.Filter.Eq(x => x.PlayerId, string.Empty),
            Builders<PlayerDoc>.Filter.Exists(x => x.PlayerId, false));
        var filter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            notClaimed);
        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var update = Builders<PlayerDoc>.Update.Set(x => x.PlayerId, targetPlayerId);
            var updatedDoc = await service.Players.FindOneAndUpdateAsync(filter, update, options);
            if (updatedDoc != null)
            {
                Log.Debug($"PlayerId 签发/认领成功 account={accountId} playerId={updatedDoc.PlayerId} source={sourceTag}");
                return (0u, string.Empty, updatedDoc.PlayerId);
            }

            // filter 未命中:并发场景下另一路已抢先写入,回读当前 doc 拿权威值。
            var currentDoc = await service.Players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId))
                .FirstOrDefaultAsync();
            if (currentDoc != null && !string.IsNullOrEmpty(currentDoc.PlayerId))
            {
                if (!string.IsNullOrEmpty(localPlayerId) && localPlayerId != currentDoc.PlayerId)
                {
                    Log.Warning($"PlayerId 不一致(并发回读) account={accountId} server={currentDoc.PlayerId} client={localPlayerId}");
                }
                return (0u, string.Empty, currentDoc.PlayerId);
            }

            Log.Warning($"PlayerId 签发后回读失败 account={accountId}");
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }
        catch (MongoWriteException mwe) when (mwe.WriteError != null && mwe.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // TOCTOU 窗口被 partial unique index 拒绝(查重时未占用 / 写入前被另一并发认领抢占)。
            // C# 驱动会按场景把 duplicate-key 包成 MongoWriteException 或 MongoCommandException(Code=11000),
            // 两种形态都走同一退回逻辑(同 Rank/Activity/Mail/Redeem 双 catch 范式)。
            return await FallbackIssuePlayerId(service, accountId, targetPlayerId, filter, options);
        }
        catch (MongoCommandException e) when (e.Code == 11000 /* DuplicateKey */)
        {
            return await FallbackIssuePlayerId(service, accountId, targetPlayerId, filter, options);
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceHelper.ClaimOrIssuePlayerId 失败 account={accountId},err={e.Message}");
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }
    }

    /// <summary>
    /// PlayerId 认领命中 DB 唯一索引冲突后的退回路径:服务端生成新 guid 重写。
    /// 抽出独立方法以让 MongoWriteException / MongoCommandException 两种 duplicate-key catch 共用同一退回逻辑。
    /// 新 guid 碰撞概率 = 2^-128,实战 = 0,不再循环重试。
    /// </summary>
    private static async FTask<(uint errorCode, string message, string playerId)> FallbackIssuePlayerId(
        PlayerPropertyServiceComponent service, string accountId, string originalTarget,
        FilterDefinition<PlayerDoc> filter, FindOneAndUpdateOptions<PlayerDoc> options)
    {
        Log.Warning($"PlayerId 认领写入命中 DB 唯一索引冲突 account={accountId} target={originalTarget} → 退回服务端生成。");
        // 调用方进入本方法前已守卫 service.Players != null(ClaimOrIssuePlayerId 入口处 early return)。
        var players = service.Players!;
        try
        {
            var fallbackId = Guid.NewGuid().ToString("N");
            var fallbackUpdate = Builders<PlayerDoc>.Update.Set(x => x.PlayerId, fallbackId);
            var fallbackDoc = await players.FindOneAndUpdateAsync(filter, fallbackUpdate, options);
            if (fallbackDoc != null)
            {
                Log.Debug($"PlayerId 退回服务端生成成功 account={accountId} playerId={fallbackDoc.PlayerId} source=server-issue(fallback)");
                return (0u, string.Empty, fallbackDoc.PlayerId);
            }

            // 退回写入仍未命中 filter:并发回读拿权威值。
            var currentDoc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId))
                .FirstOrDefaultAsync();
            if (currentDoc != null && !string.IsNullOrEmpty(currentDoc.PlayerId))
            {
                return (0u, string.Empty, currentDoc.PlayerId);
            }

            Log.Warning($"PlayerId 退回服务端生成后回读失败 account={accountId}");
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceHelper.ClaimOrIssuePlayerId 退回写入失败 account={accountId},err={e.Message}");
            return (1u, "playerId 签发失败:数据服务不可用", string.Empty);
        }
    }

    /// <summary>
    /// 通用变更入口(PropertyChangeRequest handler / 进程内 API 共用,plan D7 + §3.5)。
    ///
    /// 校验顺序(§3.4 校验体系):
    ///   1. 服务就绪(组件 / players 句柄非空);
    ///   2. 类型枚举合法(三类之一);
    ///   3. delta 范围合法([-类型上界, +类型上界],防 long 极值绕过余额检查,§5.5);
    ///   4. 限界信任(仅 serverAuthoritative=false 时生效,客户端 RPC 路径专用):
    ///      ·SingleDeltaLimit 单笔幅度上限;
    ///      ·同 account|type 100ms 频率闸(进程内字典,防客户端 spam);
    ///   5. MongoDB 单条原子 FindOneAndUpdate:
    ///      filter: _id == accountId AND 0 <= 当前余额 + delta <= 类型上界
    ///      update: $inc(该属性, delta) + $set(LastChangeUnixMs)
    ///      ReturnDocument: After(成功 → 取新余额回包);
    ///   6. 匹配失败 → 按 delta 正负返 NotEnough / OverLimit + 含当前实际余额(§3.4)。
    ///
    /// serverAuthoritative:
    ///   false(默认)= 客户端 RPC 入口,跑限界信任(单笔上限 + 频率闸);
    ///   true        = 服务端进程内权威发放(订单结算 / 活动 / 邮件等业务系统调用),
    ///                 跳过 SingleDeltaLimit + 频率闸(发奖金额由服务端自己算定,不是客户端上报;
    ///                 不同槽位连交两单时第二单不应被限速误拒)。
    ///                 上界 cap、范围校验、原子写仍生效,**不**降低权威性。
    ///
    /// 返回 (resultCode, newAmount):
    ///   - Success:newAmount = 变更后新余额;
    ///   - NotEnough / OverLimit:newAmount = 当前实际余额(便于客户端段下一刀 toast「需要 X,你有 Y」);
    ///   - 其它失败:newAmount = 0。
    ///
    /// 仅 Success 时由调用方起推送(`SendDeltaPushTo`);本方法**不**直接起推送
    /// (调用方上下文不同:handler 在请求路径,进程内 API 在业务系统刀,推送时机由调用方控)。
    ///
    /// writeLedger:
    ///   true(默认)= 成功后旁路追加一行属性流水(player_attr_ledger),有意义交易(购买 / 奖励 / GM / 货币收支)审计留痕。
    ///   false       = 不写流水。仅用于**高频派生型低价值**变更(如落子每步派生的体力增减)——每步一行会使流水无界暴涨、
    ///                 且体力由落子确定性可推、盘面文档已隐含记录,细粒度审计价值低。余额权威变更本身不受影响(照常原子落库 + 推送)。
    /// </summary>
    public static async FTask<(PropertyChangeResultCode resultCode, long newAmount)> ChangeProperty(
        Scene scene, string accountId, PropertyType type, long delta, string reason,
        bool serverAuthoritative = false, bool writeLedger = true)
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

        // 类型合法性:七类枚举之一(P2 扩到七类,SV10)。
        if (!TryGetTypeMeta(service, type, out var fieldName, out var upperBound, out var singleDeltaLimit))
        {
            return (PropertyChangeResultCode.UnknownType, 0L);
        }

        // delta 范围合法性:[-上界, +上界],防 long.MaxValue 极值溢出绕过条件过滤(§3.4 + §5.5)。
        if (delta < -upperBound || delta > upperBound)
        {
            return (PropertyChangeResultCode.InvalidRequest, 0L);
        }

        // P2 限界信任:仅客户端 RPC 路径(serverAuthoritative=false)跑;
        // 服务端权威进程内发放(订单 / 活动 / 邮件等)金额由服务端自己算定、reason 由代码侧固定,
        // 不受「客户端 spam / 客户端粗暴改值」威胁;且业务真实多笔奖励连发(如同账号 100ms 内连交两单)
        // 会被同 type 100ms 频率闸误拒,需绕过。
        if (!serverAuthoritative)
        {
            // 单次 delta 上限:|delta| 超过 singleDeltaLimit → 视为客户端粗暴改值,拒。
            if (delta > singleDeltaLimit || delta < -singleDeltaLimit)
            {
                Log.Warning($"PropertyChange 拒因=SingleDeltaLimit account={accountId} type={type} delta={delta} limit={singleDeltaLimit} reason='{reason}'");
                return (PropertyChangeResultCode.InvalidRequest, 0L);
            }
        }

        var nowMs = TimeHelper.Now;

        // 频率限制:同账号同属性最小间隔(进程内字典,沿 P1 RankAntiCheatPolicy 同款做法)。
        // 拒因复用 InvalidRequest,日志标 RateLimited 拒因(避免新增结果码扩大客户端段下一刀处理面)。
        if (!serverAuthoritative && service.PropertyChangeMinIntervalMs > 0L)
        {
            var rateKey = string.Concat(accountId, "|", ((int)type).ToString());
            if (service.LastChangeAtMs.TryGetValue(rateKey, out var prevMs))
            {
                if (nowMs - prevMs < service.PropertyChangeMinIntervalMs)
                {
                    Log.Warning($"PropertyChange 拒因=RateLimited account={accountId} type={type} delta={delta} elapsed={nowMs - prevMs}ms min={service.PropertyChangeMinIntervalMs}ms reason='{reason}'");
                    return (PropertyChangeResultCode.InvalidRequest, 0L);
                }
            }
            service.LastChangeAtMs[rateKey] = nowMs;
        }

        // Energy 类型:在变更前先按 EnergyLastRecoverMs 流逝时间做恢复结算(P2 体力服务端权威),
        // 否则后续 FindOneAndUpdate 用条件过滤拿到的当前余额是「未补恢复」的旧值,delta 判断会偏。
        if (type == PropertyType.Energy)
        {
            // 先读 doc 取 EnergyLastRecoverMs(也供 RecoverEnergyIfDue 做 CAS filter)。
            try
            {
                var preDoc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
                if (preDoc != null)
                {
                    await RecoverEnergyIfDue(service, accountId, preDoc, nowMs);
                }
            }
            catch (MongoException e)
            {
                Log.Warning($"PropertyChange 体力变更前恢复结算读取失败 account={accountId},err={e.Message}");
                // 不阻断:就算恢复结算没做成,后续 FindOneAndUpdate 仍按当前 DB 值原子裁决(语义降级但不崩)。
            }
        }
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
                // writeLedger=false(高频派生型变更,如落子每步体力)跳过追加,防流水无界暴涨;余额权威变更不受影响。
                if (writeLedger)
                {
                    await AttrLedgerHelper.AppendAsync(
                        service, accountId, type,
                        balanceBefore: newAmount - delta,
                        balanceAfter: newAmount,
                        delta: delta,
                        reasonRaw: reason ?? string.Empty,
                        timestampMs: nowMs);
                }

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
    /// 读玩家权威体力(先结算被动恢复,再返回结算后余额)。落子体力服务端派生用:
    /// 需要一个「与 ChangeProperty 同源、同 nowMs 恢复结算口径」的当前体力 E,才能算出命中绝对目标体力的 netDelta。
    ///
    /// 恢复结算口径与 ChangeProperty 完全一致(同一个 RecoverEnergyIfDue + 同一 nowMs = TimeHelper.Now):
    /// 本方法先 RecoverEnergyIfDue 把体力补到最新,随后调用方紧接着(读 → 算 netDelta 之间无 await)调 ChangeProperty,
    /// ChangeProperty 内部再次 RecoverEnergyIfDue 因 elapsed &lt; interval 变为 no-op(时间闸只按整 tick 前进,同 nowMs 不重复补),
    /// 故 ChangeProperty 的 $inc(netDelta) 落在本方法读到的 E 上、命中调用方算定的目标体力。
    ///
    /// 返回 (ok, energy):
    ///   - ok=true:energy = 结算后当前体力(供派生 netDelta);
    ///   - ok=false:服务不可用 / 账号未首登 / MongoDB 不可达,energy=0(调用方按服务不可用处理,不派生体力)。
    /// </summary>
    public static async FTask<(bool ok, long energy)> ReadEnergyAuthoritative(Scene scene, string accountId)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (false, 0L);
        }

        var nowMs = TimeHelper.Now;
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null)
            {
                // 未首登(理论上落子前必已登录,防御性):无档可读,按服务不可用返。
                return (false, 0L);
            }
            // 先结算被动恢复(会就地更新 doc.Energy),返回结算后体力。
            await RecoverEnergyIfDue(service, accountId, doc, nowMs);
            return (true, doc.Energy);
        }
        catch (MongoException e)
        {
            Log.Warning($"PlayerPropertyServiceHelper.ReadEnergyAuthoritative 失败 account={accountId},err={e.Message}");
            return (false, 0L);
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
    /// 同 <see cref="SendDeltaPushTo"/>,但**排除发起会话**(<paramref name="exceptSession"/>)。批量变更 handler 用:
    /// 发起方已从批量响应拿到全部权威值,不必再收自身的逐项 delta-push(除冗余自推);该账号其它在线会话(若有)仍收到对齐。
    /// 单会话模型下 <c>account.Session</c> 即发起方 → 不推(发起方唯一会话)。离线 / 会话已断 → 丢弃(下次登录快照对齐)。
    /// </summary>
    public static void SendDeltaPushToExcept(Scene scene, string accountId, Session exceptSession,
        PropertyType type, long newAmount, string reason)
    {
        if (!AccountManageHelper.TryGetAccount(scene, accountId, out var account))
        {
            return;
        }

        Session session = account.Session;
        if (session == null || session.IsDisposed)
        {
            return;
        }

        // 排除发起会话:它已从批量响应拿到权威值,自推冗余(单会话模型下 account.Session 即发起方 → 直接不推)。
        if (exceptSession != null && session.RuntimeId == exceptSession.RuntimeId)
        {
            return;
        }

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
        info.RenameCount = doc.RenameCount;
        info.SchemaVersion = PlayerPropertyServiceComponent.CurrentSchemaVersion;
        // 头像 / 框服务端权威(2c):当前佩戴 id + 已解锁集合并入登录快照,客户端据此拿权威初值(替代从 blob 读)。
        info.CurrentAvatarId = doc.CurrentAvatarId;
        info.CurrentFrameId = doc.CurrentFrameId;
        if (doc.UnlockedAvatarIds != null) info.UnlockedAvatarIds.AddRange(doc.UnlockedAvatarIds);
        if (doc.UnlockedFrameIds != null) info.UnlockedFrameIds.AddRange(doc.UnlockedFrameIds);
        // 祈愿服务端权威(3a):今日次数(InitOrLoad 已跑 ResetWishIfDue,doc.WishUsedToday 为重置后当日值)+ 每日上限,
        //   客户端据此算今日剩余祈愿次数(替代从 blob 读)。
        info.WishUsedToday = doc.WishUsedToday;
        info.WishDailyLimit = WishConfigServer.WishDailyLimit;
        // 看广告领体力每日闸服务端权威:今日次数(InitOrLoad 已跑 ResetAdEnergyIfDue,doc.AdEnergyUsedToday 为重置后当日值)+ 每日上限,客户端据此算今日剩余次数。
        info.AdEnergyUsedToday = doc.AdEnergyUsedToday;
        info.AdEnergyDailyLimit = AdEnergyConfigServer.DailyMax;
        // 钻石购买体力每日闸服务端权威(Round F):今日次数(InitOrLoad 已跑 ResetBuyEnergyIfDue,为重置后当日值)+ 每日上限,客户端据此算今日剩余。
        info.BuyEnergyUsedToday = doc.BuyEnergyUsedToday;
        info.BuyEnergyDailyLimit = BuyEnergyConfigServer.DailyLimit;
        // 皮肤态 / 神庙装饰标志服务端权威(3b):三态并入登录快照,客户端据此拿权威初值(替代从 blob 读)。
        info.SkinMono = doc.SkinMono;
        info.SkinMonoId = doc.SkinMonoId;
        info.TempleDecorated = doc.TempleDecorated;
        AddProperty(info, PropertyType.Diamond, doc.Diamond);
        // P2 四种玩法货币也并入登录快照。Energy 已由 InitOrLoad 调 RecoverEnergyIfDue 结算过,doc.Energy 为最新值。
        AddProperty(info, PropertyType.SoulPower, doc.SoulPower);
        AddProperty(info, PropertyType.Piety, doc.Piety);
        AddProperty(info, PropertyType.GuardianExp, doc.GuardianExp);
        AddProperty(info, PropertyType.Energy, doc.Energy);
        // P3 五元层进度计数器并入登录快照,客户端据此拿权威初值(替代从 blob 读)。
        AddProperty(info, PropertyType.GoddessRating, doc.GoddessRating);
        AddProperty(info, PropertyType.UnlockedChapter, doc.UnlockedChapter);
        AddProperty(info, PropertyType.BlindBoxCount, doc.BlindBoxCount);
        AddProperty(info, PropertyType.TempleRepaired, doc.TempleRepaired);
        AddProperty(info, PropertyType.NextRepairIndex, doc.NextRepairIndex);

        // 背包服务端权威:两轨全量并入登录快照(批次轨按 ServerNowMs 剔除过期)。
        // ServerNowMs = 服务端权威当前时刻,客户端据此锚定批次倒计时基准(不信客户端本地时钟)。
        // InventoryLoaded=true:doc 非空即权威整份(SendPlayerInfoTo 入口已 doc==null 早退),客户端可整份覆盖投影。
        var nowMs = TimeHelper.Now;
        foreach (var h in InventoryServiceHelper.BuildHoldingMessages(doc)) info.Holdings.Add(h);
        foreach (var lot in InventoryServiceHelper.BuildLotMessages(doc, nowMs)) info.Lots.Add(lot);
        info.ServerNowMs = nowMs;
        info.InventoryLoaded = true;
        // 使用幂等锚当前值:客户端登录据此 seed 本地 reqSeq 底,保重登后首个使用序号严格大于服务端已处理值,不受客户端时钟回拨影响。
        info.LastUseReqSeq = doc.LastUseReqSeq;

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
    /// 取某属性的 BSON 字段名 + 类型上界(背包使用事务把货币产出 $inc 折叠进「扣道具」原子命令时,
    /// 需要与 ChangeProperty 同源的字段名 + 上界做 filter 夹界)。未知类型返 false。
    /// 复用 TryGetTypeMeta,不重复实现字段映射。
    /// </summary>
    public static bool TryGetPropertyFieldMeta(PlayerPropertyServiceComponent service, PropertyType type,
        out string fieldName, out long upperBound)
    {
        return TryGetTypeMeta(service, type, out fieldName, out upperBound, out _);
    }

    /// <summary>读某属性在文档中的当前余额(背包使用产出货币后取新余额供推送 / 响应)。复用 GetFieldValue。</summary>
    public static long ReadPropertyValue(PlayerDoc doc, PropertyType type) => GetFieldValue(doc, type);

    /// <summary>
    /// 取出某类型对应的 PlayerDoc BSON 字段名 + 配置上界 + 单次 delta 上限(P2 限界信任新增)。
    /// 字段名按 BSON 序列化默认 = C# 属性名(无 [BsonElement] 重命名,PlayerDoc 没用)。
    /// 未知类型返 false → 触发 UnknownType 结果码(§3.4 + SV10)。
    /// </summary>
    private static bool TryGetTypeMeta(PlayerPropertyServiceComponent service, PropertyType type,
        out string fieldName, out long upperBound, out long singleDeltaLimit)
    {
        switch (type)
        {
            case PropertyType.Diamond:
                fieldName = nameof(PlayerDoc.Diamond);
                upperBound = service.DiamondUpperBound;
                singleDeltaLimit = service.DiamondSingleDeltaLimit;
                return true;
            case PropertyType.SoulPower:
                fieldName = nameof(PlayerDoc.SoulPower);
                upperBound = service.SoulPowerUpperBound;
                singleDeltaLimit = service.SoulPowerSingleDeltaLimit;
                return true;
            case PropertyType.Piety:
                fieldName = nameof(PlayerDoc.Piety);
                upperBound = service.PietyUpperBound;
                singleDeltaLimit = service.PietySingleDeltaLimit;
                return true;
            case PropertyType.GuardianExp:
                fieldName = nameof(PlayerDoc.GuardianExp);
                upperBound = service.GuardianExpUpperBound;
                singleDeltaLimit = service.GuardianExpSingleDeltaLimit;
                return true;
            case PropertyType.Energy:
                fieldName = nameof(PlayerDoc.Energy);
                upperBound = service.EnergyUpperBound;
                singleDeltaLimit = service.EnergySingleDeltaLimit;
                return true;
            // P3 五元层进度计数器:同套原子写 / 限界信任(纯 $inc 计数器,无体力式恢复结算)。
            case PropertyType.GoddessRating:
                fieldName = nameof(PlayerDoc.GoddessRating);
                upperBound = service.GoddessRatingUpperBound;
                singleDeltaLimit = service.GoddessRatingSingleDeltaLimit;
                return true;
            case PropertyType.UnlockedChapter:
                fieldName = nameof(PlayerDoc.UnlockedChapter);
                upperBound = service.UnlockedChapterUpperBound;
                singleDeltaLimit = service.UnlockedChapterSingleDeltaLimit;
                return true;
            case PropertyType.BlindBoxCount:
                fieldName = nameof(PlayerDoc.BlindBoxCount);
                upperBound = service.BlindBoxCountUpperBound;
                singleDeltaLimit = service.BlindBoxCountSingleDeltaLimit;
                return true;
            case PropertyType.TempleRepaired:
                fieldName = nameof(PlayerDoc.TempleRepaired);
                upperBound = service.TempleRepairedUpperBound;
                singleDeltaLimit = service.TempleRepairedSingleDeltaLimit;
                return true;
            case PropertyType.NextRepairIndex:
                fieldName = nameof(PlayerDoc.NextRepairIndex);
                upperBound = service.NextRepairIndexUpperBound;
                singleDeltaLimit = service.NextRepairIndexSingleDeltaLimit;
                return true;
            default:
                fieldName = string.Empty;
                upperBound = 0L;
                singleDeltaLimit = 0L;
                return false;
        }
    }

    /// <summary>取出 PlayerDoc 中某类型对应的余额字段值。</summary>
    private static long GetFieldValue(PlayerDoc doc, PropertyType type)
    {
        return type switch
        {
            PropertyType.Diamond => doc.Diamond,
            PropertyType.SoulPower => doc.SoulPower,
            PropertyType.Piety => doc.Piety,
            PropertyType.GuardianExp => doc.GuardianExp,
            PropertyType.Energy => doc.Energy,
            PropertyType.GoddessRating => doc.GoddessRating,
            PropertyType.UnlockedChapter => doc.UnlockedChapter,
            PropertyType.BlindBoxCount => doc.BlindBoxCount,
            PropertyType.TempleRepaired => doc.TempleRepaired,
            PropertyType.NextRepairIndex => doc.NextRepairIndex,
            _ => 0L
        };
    }

    /// <summary>
    /// 体力被动恢复懒结算(P2):变更/查询 Energy 之前,按 EnergyLastRecoverMs 与 nowMs 流逝时间补恢复量。
    /// 镜像客户端 MergeOrderState.ApplyTimeRegen 语义(规则:只有被动恢复有 SoftCap 软上限,主动来源可超):
    ///   - doc.Energy &lt; SoftCap:$set(Energy, min(Energy + ticks*perTick, SoftCap)) + 推进 EnergyLastRecoverMs;
    ///   - doc.Energy &gt;= SoftCap:**不**写 Energy(保留主动来源溢出的盈余),**仍**推进 EnergyLastRecoverMs
    ///     (不囤积流逝的 ticks;一旦消费降到 SoftCap 以下,从那刻起重新累计)。
    /// CAS:filter 含 EnergyLastRecoverMs == lastMs 防并发双结算(SV11 沿三老属性原子单写范式)。
    /// 旧文档兼容:doc.EnergyLastRecoverMs == 0(P0/P1 期玩家无此字段,BSON 反序列化默认 0)→
    /// 视为「首次接触新字段」,写入 nowMs 而不补恢复量(防 nowMs - 0 = epoch 流逝直接回满)。
    /// 失败(MongoDB 不可达 / 抖动)→ Warning 不抛、不阻断后续 ChangeProperty(返回的 doc 仍可用)。
    /// </summary>
    private static async FTask RecoverEnergyIfDue(
        PlayerPropertyServiceComponent service, string accountId, PlayerDoc doc, long nowMs)
    {
        var players = service.Players;
        if (players == null) return;

        var lastMs = doc.EnergyLastRecoverMs;

        // 边界:旧文档缺字段(反序列化为 0)或新登时被设为 nowMs。
        // lastMs == 0 = 旧档兼容:不补恢复、直接刷字段为 nowMs。
        if (lastMs <= 0L)
        {
            var bootstrapFilter = Builders<PlayerDoc>.Filter.And(
                Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
                Builders<PlayerDoc>.Filter.Eq(x => x.EnergyLastRecoverMs, 0L));
            var bootstrapUpdate = Builders<PlayerDoc>.Update.Set(x => x.EnergyLastRecoverMs, nowMs);
            try
            {
                var newDoc = await players.FindOneAndUpdateAsync(bootstrapFilter, bootstrapUpdate,
                    new FindOneAndUpdateOptions<PlayerDoc> { IsUpsert = false, ReturnDocument = ReturnDocument.After });
                if (newDoc != null)
                {
                    doc.EnergyLastRecoverMs = newDoc.EnergyLastRecoverMs;
                    doc.Energy = newDoc.Energy;
                }
            }
            catch (MongoException e)
            {
                Log.Warning($"RecoverEnergyIfDue 旧档 bootstrap 失败 account={accountId},err={e.Message}");
            }
            return;
        }

        var elapsed = nowMs - lastMs;
        var interval = service.EnergyRecoverIntervalMs;
        if (elapsed < interval) return;

        var ticks = elapsed / interval;
        var advanceMs = ticks * interval;
        var newLastMs = lastMs + advanceMs;
        var softCap = service.EnergyRecoverSoftCap;
        var perTick = service.EnergyRecoverPerTick;

        // CAS filter 共用:_id 锚 + EnergyLastRecoverMs == lastMs 防并发双结算。
        var casFilter = Builders<PlayerDoc>.Filter.And(
            Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId),
            Builders<PlayerDoc>.Filter.Eq(x => x.EnergyLastRecoverMs, lastMs));
        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        // doc.Energy >= softCap:只推进 lastMs、不动 Energy(镜像客户端 Energy < cap 块外仍推进 LastEnergyRegenTime;
        // 关键差异于旧实现:旧代码无条件 Set(Energy, min(Energy+restored, cap)),Energy 已超 softCap 时会被钳回,
        // 与"主动来源可超 30"规则冲突 — 订单交付攒下的盈余会被下一次被动恢复抹掉)。
        if (doc.Energy >= softCap)
        {
            var update = Builders<PlayerDoc>.Update.Set(x => x.EnergyLastRecoverMs, newLastMs);
            try
            {
                var newDoc = await players.FindOneAndUpdateAsync(casFilter, update, options);
                if (newDoc != null)
                {
                    doc.EnergyLastRecoverMs = newDoc.EnergyLastRecoverMs;
                    doc.Energy = newDoc.Energy;
                    Log.Debug($"Energy 恢复结算跳过(已超软上限) account={accountId} energy={newDoc.Energy} softCap={softCap} lastRecoverMs={newDoc.EnergyLastRecoverMs}");
                }
            }
            catch (MongoException e)
            {
                Log.Warning($"RecoverEnergyIfDue 推进 lastMs 失败 account={accountId} ticks={ticks},err={e.Message}");
            }
            return;
        }

        // doc.Energy < softCap:补恢复并钳到 softCap;条件过滤 + $set 直接置目标值,而非 $inc 防超 softCap。
        var targetEnergy = doc.Energy + ticks * perTick;
        if (targetEnergy > softCap) targetEnergy = softCap;

        var setUpdate = Builders<PlayerDoc>.Update
            .Set(x => x.Energy, targetEnergy)
            .Set(x => x.EnergyLastRecoverMs, newLastMs);
        try
        {
            var newDoc = await players.FindOneAndUpdateAsync(casFilter, setUpdate, options);
            if (newDoc != null)
            {
                doc.Energy = newDoc.Energy;
                doc.EnergyLastRecoverMs = newDoc.EnergyLastRecoverMs;
                Log.Debug($"Energy 恢复结算 account={accountId} ticks={ticks} energy={newDoc.Energy} lastRecoverMs={newDoc.EnergyLastRecoverMs}");
            }
            // newDoc == null:并发已被另一路结算,本路当作已完成(下次变更时取到最新 doc)。
        }
        catch (MongoException e)
        {
            Log.Warning($"RecoverEnergyIfDue 结算失败 account={accountId} ticks={ticks},err={e.Message}");
        }
    }

}
