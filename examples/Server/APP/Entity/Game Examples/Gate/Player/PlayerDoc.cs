using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 玩家属性账本的原生 MongoDB 文档定义(非框架 Entity)。
// 服务端权威三属性(金币 / 钻石 / 体力)持久存储,主键 = UUID,与 accounts._id 1:1 同源。
// 变更必须用 MongoDB 单条原子 FindOneAndUpdate(条件过滤 + $inc + $set),防并发同账号双扣超发(SV11)。
// 框架 IDatabase 高层 API 只能先读后写(设计明令禁止),故直接用原生 IMongoCollection<PlayerDoc>。
// 设计基线:design-docs/37-player-attr-server.md §3.1。

/// <summary>
/// 玩家属性账本文档:每 UUID 一行,记三属性余额 + 末次变更时间 + schema 版本。
/// 集合 players;_id = AccountId(与 accounts._id 同值,1:1 关联,SV3)。
/// 主键 MongoDB 天然唯一,无额外索引(本子单无按余额查询需求,Tier 1+ 真要做「金币 >= N 的玩家」类运营查询时再加,SV2)。
/// 首登 setOnInsert 写初始值(金币 0 / 钻石 0 / 体力 5 默认,以 PlayerPropertyServiceComponent 配置为准);
/// 重登走 update 路径但 update 命令完全不触碰余额字段,余额稳定 = 上次变更后的值(SV5)。
/// </summary>
public sealed class PlayerDoc
{
    /// <summary>账号 id(= UUID 字符串,与 accounts._id 同源,SV3),作为 _id 主键。</summary>
    [BsonId]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// 玩家身份 ID(账号级稳定唯一标识,服务端权威签发)。
    /// 首登时按客户端上交的 localPlayerId 认领(非空)或服务端新生成(空);此后跨登录恒稳定不变。
    /// 旧文档缺字段时初始化器保底空串,登录处理链遇空会触发签发/认领写回(InitOrLoad 之后由调用方处理)。
    /// </summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>钻石余额(非负;原子 FindOneAndUpdate 用条件过滤保证 0 <= 余额 + delta <= 上界,SV8/SV9/SV11)。</summary>
    public long Diamond { get; set; }

    /// <summary>昵称(首登 setOnInsert 默认空串;旧文档缺字段时初始化器保底为空串)。</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>等级(首登 setOnInsert 默认 1;旧文档缺字段时初始化器保底为 1)。</summary>
    public int Level { get; set; } = 1;

    /// <summary>经验(首登 setOnInsert 默认 0;旧文档缺字段保底 0)。</summary>
    public long Exp { get; set; }

    /// <summary>
    /// 改名次数(首登 setOnInsert 默认 0;旧文档缺字段保底 0)。服务端权威:改名费按此值派生
    /// (0 = 首次免费,>0 = 按 RenameConfigServer.PriceFor(RenameCount) 收费),客户端不上报费用/次数。
    /// 每次改名成功后 +1,与 Nickname 在同一条原子 update 内一起写(改名裁定单条 FindOneAndUpdate)。
    /// </summary>
    public int RenameCount { get; set; }

    /// <summary>末次属性变更时间(服务端 Unix 毫秒,UTC)。每次 $inc 成功后 $set 刷新;Tier 2+ ledger 落地前作单字段快照。</summary>
    public long LastChangeUnixMs { get; set; }

    // ---- P2 新增:四种玩法货币并轨服务端权威(2026-06 全栈迁移)----
    // 与既有 Diamond/Level/Exp 彼此独立、不复用、不映射。
    // 旧文档反序列化时缺字段 → MongoDB.Bson 默认值(数值 = 0、long = 0L)即合理初值。
    // 命名约定:守护者经验用 GuardianExp 与 PlayerDoc.Exp(玩家账号经验)显式区分。

    /// <summary>灵力(玩法软货币;非负,与 Diamond 同条件过滤范式落账)。</summary>
    public long SoulPower { get; set; }

    /// <summary>虔诚币(长期主线货币;非负,纯增减计数器)。</summary>
    public long Piety { get; set; }

    /// <summary>守护者累积经验(玩法侧第四种货币,与玩家账号经验 Exp 不复用)。</summary>
    public long GuardianExp { get; set; }

    /// <summary>玩法体力余额(非负;带离线随时间恢复,服务端在查询/变更前按 EnergyLastRecoverMs 懒结算)。</summary>
    public long Energy { get; set; }

    /// <summary>体力上次恢复结算时刻(Unix 毫秒,UTC)。首登 setOnInsert 为 nowMs;每次结算成功后刷新到结算所覆盖的整数倍 tick 末端。</summary>
    public long EnergyLastRecoverMs { get; set; }

    // ---- P2 Phase 1·normal 订单·服务端权威订单进度状态 ----
    // 服务端不持「激活订单数组」(可由 OrderCursor + DeliveredMask 派生:激活订单 = pool[(cursor - ActiveOrders + i) % poolLen],
    // 已交付的槽按 mask 位置空)。这样订单池配置/数组长度改了不会让旧 doc 失配,且不冗余存可推导的数据。
    //
    // 旧文档反序列化时三字段缺失 → BSON 默认 0 / 0L:OrderCursor==0 等价"首次,游标在 0";LastOrderRefreshMs==0 等价"尚无记录,
    // 首次结算以 nowMs 初始化、本次不刷"(同 EnergyLastRecoverMs 的 bootstrap 语义);DeliveredMask==0 等价"无槽位已交付"。
    // 三者都不需要 partial index $ne 配合(数值字段 default==0 直接是合理初值,绕开了字符串 absent vs 空串陷阱)。

    /// <summary>
    /// 订单池游标(下一张未取的索引,服务端按池长 OrderPool.Length 取模)。
    /// 每次刷新一批 = cursor += ActiveOrders;交付不动 cursor(只置 DeliveredMask 位)。
    /// 旧档缺字段 → 0 等价首次。
    /// </summary>
    public int OrderCursor { get; set; }

    /// <summary>
    /// 订单上次整批刷新时刻(Unix 毫秒,UTC,服务端权威时钟)。
    /// 刷新触发 = 经过 ≥ OrderRefreshIntervalMs 真实毫秒,整批替换激活订单(cursor 推进 ActiveOrders 张)+ 清 DeliveredMask + LastOrderRefreshMs=newMs。
    /// 旧档缺字段 → 0L 等价"尚无记录,首次接触时 bootstrap 为 nowMs、本次不刷"(同 EnergyLastRecoverMs)。
    /// </summary>
    public long LastOrderRefreshMs { get; set; }

    /// <summary>
    /// 本轮已交付订单的 bitmask(bit i = 第 i 槽已交付,槽 ∈ [0, ActiveOrders))。
    /// 交付成功置位;整批刷新时清零。ActiveOrders=3 时仅用低 3 位,int32 足够。
    /// 旧档缺字段 → 0 等价"本轮无交付"(配合 OrderCursor==0 首次,等价首批未交付)。
    /// </summary>
    public int OrderDeliveredMask { get; set; }

    // ---- P3 元层进度计数器·服务端权威(原云存档 blob 迁出第 1 批,2026-07 全栈迁移)----
    // 五个纯数值进度计数器,与既有货币/体力彼此独立、不复用。走同一套 PropertyChange($inc + 限界信任 + ledger + 推送)。
    // 用 long(非 int)与既有余额字段同类型:变更走 ChangeProperty 的 long 过滤 / $inc / GetFieldValue 统一路径,
    //   避免 int32 字段与 long delta 在 MongoDB filter(Gte(field, -delta))产生 BSON 数值类型不匹配。
    // 旧文档反序列化缺字段 → BSON 默认 0L 即合理初值(全新玩家这些进度本就为 0);首登 setOnInsert 显式写 0 使字段 present。
    // 语义单调性:GoddessRating/UnlockedChapter/TempleRepaired/NextRepairIndex 单调递增(玩法/动作只增不减);
    //   BlindBoxCount 可增可减(攒盒 + / 开盒 -)。骨架限界信任只防异常大跳,不强制单调(宽松,拿不准从宽,见服务组件阈值注)。

    /// <summary>女神评级(玩法产出;单调递增,缺省 0)。</summary>
    public long GoddessRating { get; set; }

    /// <summary>章节解锁数(玩法产出;单调递增,缺省 0)。</summary>
    public long UnlockedChapter { get; set; }

    /// <summary>盲盒计数(玩法产出;可增可减 —— 攒盒 + / 开盒 -,缺省 0)。</summary>
    public long BlindBoxCount { get; set; }

    /// <summary>神庙修缮计数(动作产出,修缮动作触发;单调递增,缺省 0)。</summary>
    public long TempleRepaired { get; set; }

    /// <summary>神庙修缮游标(动作产出,指向下一个待修缮项;单调递增,缺省 0)。</summary>
    public long NextRepairIndex { get; set; }

    // ---- 头像 / 头像框服务端权威(原云存档 blob 迁出第 2 批·子批 2c,2026-07)----
    // 当前佩戴 id(两个 int)+ 已解锁集合(两个 List<int>)。修饰服务端权威,解锁走 client-report 限界信任。
    // 当前 id 缺省与客户端默认对齐(头像 1 / 框 101,= 客户端 PlayerInfo.DefaultAvatarId/DefaultFrameId),
    //   使客户端登录拉快照时不会因服务端「未佩戴」误判;解锁集合缺省空,客户端登录后 bootstrap 上报默认解锁。
    // 换装:服务端校验目标 id 已在对应解锁集合内才 $set 当前 id(未解锁拒)。
    // 解锁上报:$addToSet 幂等加入集合(限界信任 sanity:id 落在合法段 + 集合大小上限防灌爆 + 频率闸)。
    // 头像与框共 id 段编排:头像 1–100 段、框 101+ 段(靠 Kind 区分,非靠 id 段;段仅作 sanity 边界)。

    /// <summary>当前佩戴头像 id(首登 setOnInsert 默认 CurrentAvatarIdInitial=1,与客户端 DefaultAvatarId 对齐;旧档缺字段补默认)。</summary>
    public int CurrentAvatarId { get; set; } = 1;

    /// <summary>当前佩戴头像框 id(首登 setOnInsert 默认 CurrentFrameIdInitial=101,与客户端 DefaultFrameId 对齐;旧档缺字段补默认)。</summary>
    public int CurrentFrameId { get; set; } = 101;

    /// <summary>已解锁头像 id 集合(首登缺省空;客户端 bootstrap 上报默认解锁后 $addToSet 幂等填入。旧档缺字段补空列表)。</summary>
    public System.Collections.Generic.List<int> UnlockedAvatarIds { get; set; } = new System.Collections.Generic.List<int>();

    /// <summary>已解锁头像框 id 集合(同上)。</summary>
    public System.Collections.Generic.List<int> UnlockedFrameIds { get; set; } = new System.Collections.Generic.List<int>();

    // ---- 祈愿(每日限领体力)服务端权威(原云存档 blob 迁出第 3 批·子批 3a,2026-07)----
    // 每日祈愿次数闸服务端权威:计数 WishUsedToday + 上次重置时刻 WishLastResetUnixMs。
    // 跨天判据用服务端本地日期(TimeHelper.Now.TransitionLocal().Date),与客户端本地日期口径对齐。
    // 灵力扣 / 体力发不在此持有(走既有 SoulPower / Energy 字段 + ChangeProperty),此处只持每日闸状态。
    // 缺省:WishUsedToday=0;WishLastResetUnixMs 首登 setOnInsert=nowMs(同 EnergyLastRecoverMs 手法,避免 0 值误判整段流逝跨天)。

    /// <summary>今日已用祈愿次数(首登 setOnInsert 默认 0;懒每日重置跨天归零)。</summary>
    public int WishUsedToday { get; set; }

    /// <summary>
    /// 上次祈愿每日重置时刻(Unix 毫秒,UTC)。跨天判据 = 本值与 nowMs 的服务端本地日期不同日。
    /// 首登 setOnInsert=nowMs;每次懒重置成功后刷新到 nowMs。旧文档缺字段反序列化为 0L,
    /// 由 InitOrLoad 补字段迁移补为 nowMs(0L 会被 TransitionLocal 判为 1970 年,与今日必跨天,虽仍收敛但补 nowMs 更稳)。
    /// </summary>
    public long WishLastResetUnixMs { get; set; }

    // ---- 看广告领体力(每日限领)服务端权威(体力系统:补广告获取源,桩流程)----
    // 与祈愿同范式的每日次数闸,独立计数(AdEnergyUsedToday + AdEnergyLastResetUnixMs),与祈愿计数彼此独立。
    // 无灵力消耗(广告免费源);发体力走既有 Energy 字段 + ChangeProperty,此处只持每日闸状态。
    // 缺省:AdEnergyUsedToday=0;AdEnergyLastResetUnixMs 首登 setOnInsert=nowMs(同 WishLastResetUnixMs 手法,避免 0L 被 TransitionLocal 判 1970 年、首登即误判跨天)。

    /// <summary>今日已用看广告领体力次数(首登 setOnInsert 默认 0;懒每日重置跨天归零)。</summary>
    public int AdEnergyUsedToday { get; set; }

    /// <summary>
    /// 上次看广告领体力每日重置时刻(Unix 毫秒,UTC)。跨天判据同祈愿(本值与 nowMs 的服务端本地日期不同日)。
    /// 首登 setOnInsert=nowMs;每次懒重置成功后刷新到 nowMs。旧文档缺字段由 MigrateSchemaIfNeeded 补 nowMs。
    /// </summary>
    public long AdEnergyLastResetUnixMs { get; set; }

    // ---- 钻石购买体力(每日限次)服务端权威(体力系统 Round F)----
    // 与祈愿 / 看广告同范式的每日次数闸,独立计数(BuyEnergyUsedToday + BuyEnergyLastResetUnixMs),与二者彼此独立。
    // 扣钻 / 发体力走既有 Diamond / Energy 字段 + ChangeProperty,此处只持每日购买闸状态。
    // 缺省:BuyEnergyUsedToday=0;BuyEnergyLastResetUnixMs 首登 setOnInsert=nowMs(同 WishLastResetUnixMs 手法,避免 0L 被 TransitionLocal 判 1970 年、首登即误判跨天)。

    /// <summary>今日已用钻石购买体力次数(首登 setOnInsert 默认 0;懒每日重置跨天归零)。</summary>
    public int BuyEnergyUsedToday { get; set; }

    /// <summary>
    /// 上次钻石购买体力每日重置时刻(Unix 毫秒,UTC)。跨天判据同祈愿(本值与 nowMs 的服务端本地日期不同日)。
    /// 首登 setOnInsert=nowMs;每次懒重置成功后刷新到 nowMs。旧文档缺字段由 MigrateSchemaIfNeeded 补 nowMs。
    /// </summary>
    public long BuyEnergyLastResetUnixMs { get; set; }

    // ---- 皮肤态 / 神庙装饰标志服务端权威(原云存档 blob 迁出第 3 批·子批 3b,2026-07)----
    // 三个「设置状态」(SET 语义,非累加计数):皮肤单色开关 + 当前单色 id + 已装饰厅数。
    // 皮肤 / 装饰纯装饰、低危,走 client-report 限界信任(存客户端上报值 + 基本 sanity),不建服务端配置自算。
    // 缺省对齐客户端默认:SkinMono=0(彩色)、SkinMonoId=-1(Unselected,彩色态客户端记 -1 且忽略)、TempleDecorated=0(无装饰)。
    // TempleDecorated 是标量已装饰厅数:客户端活态是 bool[] 前缀数组,装饰与修缮 1:1 同序耦合,已装饰集恒为前缀 [0, count),
    //   与既有 TempleRepaired 计数器同套「标量 ↔ 布尔数组」换算(客户端 MetaCurrencySync 已有转换器)。用 long 与既有计数器同类型。

    /// <summary>是否单色皮肤模式(0=彩色 / 1=单色;首登 setOnInsert 默认 0,旧档缺字段补 0)。</summary>
    public int SkinMono { get; set; }

    /// <summary>当前单色皮肤 id(彩色态 -1=Unselected;首登 setOnInsert 默认 -1,旧档缺字段补 -1)。</summary>
    public int SkinMonoId { get; set; } = -1;

    /// <summary>已装饰厅数标量(前缀语义,= 已修厅数;首登 setOnInsert 默认 0,旧档缺字段补 0)。</summary>
    public long TempleDecorated { get; set; }

    // ---- 道具持有 / 塔罗牌进度(服务端权威)----
    // 通用道具持有字典(非碎片专用):key = 道具 id 十进制字符串(BSON 文档键必须是字符串),value = 持有数量。
    // 当前业务方 = 背包堆叠道具(订单交付发放、使用扣减);原子变更走 "ItemHoldings.<itemId>" 点路径 $inc,
    // 单条 FindOneAndUpdate 保证并发安全。
    //
    // 塔罗牌进度:key = 牌 id 十进制字符串(= TbTarotCard 行),value = 已购进度步数(0..UnlockCosts.Length)。
    // 购买一步 = 单条原子 CAS(Piety 足额 + 该牌步数匹配 → $inc Piety 负扣 + $inc "TarotProgress.<cardId>" +1),
    // 每步成本服务端按 TbTarotCard.UnlockCosts[当前步] 自算;步数 == 数组长度即该牌已激活(收集态由进度派生,不另存)。
    // 缺省:首登 setOnInsert 空字典;旧档缺字段由 MigrateSchemaIfNeeded 补空字典(absent 字段对 CAS filter 永不命中)。

    /// <summary>道具持有(itemId 字符串 → 数量;首登 setOnInsert 空字典,旧档缺字段补空字典)。</summary>
    public System.Collections.Generic.Dictionary<string, long> ItemHoldings { get; set; }
        = new System.Collections.Generic.Dictionary<string, long>();

    /// <summary>塔罗牌进度(cardId 字符串 → 已购步数;首登 setOnInsert 空字典,旧档缺字段补空字典。步数 == UnlockCosts.Length 即已激活)。</summary>
    public System.Collections.Generic.Dictionary<string, int> TarotProgress { get; set; }
        = new System.Collections.Generic.Dictionary<string, int>();

    // ---- 背包批次轨 / 使用事务幂等(服务端权威)----
    // 批次轨 ItemLots 承载「有有效期」道具(每次获得一条批次,各自过期时刻),与堆叠轨 ItemHoldings 并列。
    // 使用道具是复合事务(扣道具 + 产出),LastUseReqSeq 做请求级幂等(防弱网重发双扣):
    //   客户端每次使用带单调递增 reqSeq,原子变更 filter 含 LastUseReqSeq < reqSeq、update $set = reqSeq,
    //   重复请求(reqSeq <= 已处理值)CAS 不命中 → 判重复,不二次扣。
    // InventoryVersion 是批次数组读改写的乐观并发版本:批次 FIFO 扣减需先读后算,写回时 filter 版本一致才落,
    //   防两个不同 reqSeq 的并发使用各自基于旧数组覆盖(丢失更新)。堆叠轨扣减走 $inc 条件过滤,不依赖本版本。
    // 缺省:首登 setOnInsert 空数组 / 0 / 0;旧档缺字段由 MigrateSchemaIfNeeded 补(schema < 11)。

    /// <summary>背包批次轨(有有效期道具,每次获得一条;首登 setOnInsert 空数组,旧档缺字段补空数组)。</summary>
    public System.Collections.Generic.List<ItemLot> ItemLots { get; set; }
        = new System.Collections.Generic.List<ItemLot>();

    /// <summary>末次已处理的使用请求序号(使用事务幂等锚;单调递增,首登 0,旧档缺字段补 0)。</summary>
    public long LastUseReqSeq { get; set; }

    /// <summary>背包批次数组乐观并发版本(每次批次数组变更 +1;首登 0,旧档缺字段补 0)。</summary>
    public long InventoryVersion { get; set; }

    // ---- 女神奖励溢出预算令牌(反作弊:把「溢出进背包」绑死到真实领取)----
    // 女神领取(GoddessClaimServiceHelper.TryClaim)成功时签发一次性令牌:PendingOverflowNonce = 本次领取时刻(nowMs,
    //   per-player 唯一足矣),PendingOverflowBudget = 本次奖励总量(sum TbGoddessReward.Count)。
    // C2G_GoddessOverflow 请求带 nonce,服务端原子校验 nonce 匹配 + budget >= count 才落账,并 $inc budget -count(令牌预算单次消费,
    //   用尽即失效);新一次领取覆盖 nonce + 重置 budget,旧令牌自然作废。无真实领取 → 无有效 nonce → 溢出被拒,堵住 RPC 刷取。
    // 缺省 0L = 无有效令牌:旧档缺字段反序列化为 0L,nonce(真实为非 0 nowMs)恒不匹配 → 溢出拒(正确);领取 $set 首次写入使字段 present,
    //   故无需 schema 迁移(令牌永远由领取先写、溢出后引用)。

    /// <summary>女神奖励溢出预算令牌 id(= 末次领取时刻 nowMs;0 = 无有效令牌)。溢出 RPC 须带匹配 nonce。</summary>
    public long PendingOverflowNonce { get; set; }

    /// <summary>当前令牌剩余溢出预算(= 本次奖励总量,每次溢出入库 $inc 扣减;0 = 用尽/无令牌)。</summary>
    public long PendingOverflowBudget { get; set; }

    /// <summary>schema 版本(加字段时升 + 缺字段保底)。</summary>
    public int SchemaVersion { get; set; }
}
