// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy
{
	/// <summary>
	/// Cumulative 节律累加裁决结果码(§3.3)
	/// </summary>
	public enum ActivityIncrementResultCode
	{
		/// <summary>
		/// 累加成功(counter 已写入;视情况已发奖 — 见 TargetReached 字段)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 参数非法(activityId 不存在 / delta ≤ 0 / 字段格式异常);文档零改动,不进 service
		/// </summary>
		InvalidRequest = 1,
		/// <summary>
		/// 活动配置 type ≠ Cumulative(防客户端用此 RPC 推 Login 类活动 counter,旁路登录节律)
		/// </summary>
		NotCumulative = 2,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 活动配置未加载 / Mail 服务未就绪);文档状态未知,客户端不本地放行
		/// </summary>
		ServiceUnavailable = 3
	}

	/// <summary>
	/// 看广告领体力裁决结果码
	/// </summary>
	public enum ClaimAdEnergyResultCode
	{
		/// <summary>
		/// 成功:体力已发(夹软上限)、AdEnergyUsedToday+1;响应回带最新值供客户端对账
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 今日看广告领体力次数已达上限(懒重置后仍 >= DailyMax),不发、不消耗次数
		/// </summary>
		DailyLimitReached = 2,
		/// <summary>
		/// 体力已达软上限,领了也发不进,不发、不消耗次数
		/// </summary>
		OverLimit = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 落子裁决结果码
	/// </summary>
	public enum PlaceResultCode
	{
		/// <summary>
		/// step 推进成功(baseStep == 权威 step,落子合法已执行)
		/// </summary>
		StepAdvanced = 0,
		/// <summary>
		/// 幂等命中(baseStep < 权威 step):请求是已执行步的重发,回带当前权威态、不重复执行
		/// </summary>
		IdempotentReplay = 1,
		/// <summary>
		/// 步号超前(baseStep > 权威 step):客户端落后于权威,拒绝执行、回带当前权威态供重同步
		/// </summary>
		StepAhead = 2,
		/// <summary>
		/// 落子非法(候选槽空 / 越界 / 与已占冲突):不推进、回带当前权威态
		/// </summary>
		IllegalPlacement = 3,
		/// <summary>
		/// 对局不存在(gameId 在会话上查无此局)
		/// </summary>
		GameNotFound = 4,
		/// <summary>
		/// 会话未登录(无法确定身份)
		/// </summary>
		NotLoggedIn = 5
	}

	/// <summary>
	/// 消除道具裁决结果码
	/// </summary>
	public enum ClearToolResultCode
	{
		/// <summary>
		/// 清除成功(baseStep == 权威 step,体力足额已扣,目标行列已清)
		/// </summary>
		Cleared = 0,
		/// <summary>
		/// 幂等命中(baseStep < 权威 step):已执行步的重发,回带当前权威态、不重复清、不重复扣体力
		/// </summary>
		IdempotentReplay = 1,
		/// <summary>
		/// 步号超前(baseStep > 权威 step):客户端落后于权威,拒绝执行、回带当前权威态供重同步
		/// </summary>
		StepAhead = 2,
		/// <summary>
		/// 目标格越界(row/col 不在 0..7):不清、不扣体力,回带当前权威态
		/// </summary>
		OutOfRange = 3,
		/// <summary>
		/// 体力不足(不够一次消除道具代价):不清、不扣体力,回带当前权威态(含当前体力)供回滚乐观清
		/// </summary>
		NotEnoughEnergy = 4,
		/// <summary>
		/// 对局不存在(gameId 在会话上查无此局)
		/// </summary>
		GameNotFound = 5,
		/// <summary>
		/// 会话未登录(无法确定身份)
		/// </summary>
		NotLoggedIn = 6,
		/// <summary>
		/// 服务端属性/持久服务不可用(MongoDB 不可达等):不清、不扣体力
		/// </summary>
		ServiceUnavailable = 7
	}

	/// <summary>
	/// 查询快照结果码
	/// </summary>
	public enum GameSnapshotResultCode
	{
		/// <summary>
		/// 查询成功
		/// </summary>
		Ok = 0,
		/// <summary>
		/// 对局不存在
		/// </summary>
		GameNotFound = 1,
		/// <summary>
		/// 会话未登录
		/// </summary>
		NotLoggedIn = 2
	}

	/// <summary>
	/// 购买体力裁决结果码
	/// </summary>
	public enum BuyEnergyResultCode
	{
		/// <summary>
		/// 成功:钻石已扣、体力已发;响应回带扣后钻石 + 发后体力供客户端对账
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 钻石不足,未扣钻、未发体力(响应回带当前钻石余额供 toast)
		/// </summary>
		NotEnoughDiamond = 2,
		/// <summary>
		/// 体力加满发放量后超存储上界,未扣钻、未发体力(响应回带当前体力余额)
		/// </summary>
		OverLimit = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常,未生效
		/// </summary>
		ServiceUnavailable = 4,
		/// <summary>
		/// 今日钻石购买体力次数已达上限(懒重置后仍 >= DailyLimit),未扣钻、未发、不消耗次数
		/// </summary>
		DailyLimitReached = 5
	}

	/// <summary>
	/// 修饰种类(与客户端 AvatarType 对齐:1=头像 / 2=头像框)
	/// </summary>
	public enum CosmeticKind
	{
		/// <summary>
		/// 占位(proto3 枚举须含 0;请求填此值视为非法 Kind 拒)
		/// </summary>
		CosmeticKindNone = 0,
		/// <summary>
		/// 头像
		/// </summary>
		Avatar = 1,
		/// <summary>
		/// 头像框
		/// </summary>
		Frame = 2
	}

	/// <summary>
	/// 换装裁决结果码
	/// </summary>
	public enum EquipCosmeticResultCode
	{
		/// <summary>
		/// 成功:当前佩戴 id 已切换;响应回带最新当前头像 id + 当前框 id
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// Kind 非法(非 1/2)
		/// </summary>
		InvalidKind = 2,
		/// <summary>
		/// 目标 id 不在对应已解锁集合内(未解锁,拒换)→ 客户端回退显示
		/// </summary>
		NotUnlocked = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 解锁上报裁决结果码
	/// </summary>
	public enum UnlockCosmeticResultCode
	{
		/// <summary>
		/// 成功:id 已幂等加入对应解锁集合(重复上报同 id 也返 Success)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号 → 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// Kind 非法(非 1/2)
		/// </summary>
		InvalidKind = 2,
		/// <summary>
		/// id 落在对应合法段外(sanity 拒)
		/// </summary>
		InvalidId = 3,
		/// <summary>
		/// 该已解锁集合已达大小上限(防灌爆,拒新增)
		/// </summary>
		SetFull = 4,
		/// <summary>
		/// 修饰操作频率过密(限界信任·频率闸,拒)
		/// </summary>
		RateLimited = 5,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常
		/// </summary>
		ServiceUnavailable = 6
	}

	/// <summary>
	/// 女神领取裁决结果码
	/// </summary>
	public enum GoddessClaimResultCode
	{
		/// <summary>
		/// 成功:已清零计数 + 回带奖励 payload
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 未满档(GoddessRating < 满档值)/ 并发已被领 → CAS 未命中,计数不变
		/// </summary>
		NotFull = 2,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 奖励表缺失
		/// </summary>
		ServiceUnavailable = 3
	}

	/// <summary>
	/// 女神奖励背包操作(溢出入库 / 取回)裁决结果码
	/// </summary>
	public enum GoddessBagResultCode
	{
		/// <summary>
		/// 成功(已落账 + 推送背包整份)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 非法道具(itemId 不属元素道具集 / 无配置)或入参非法(count/reqSeq ≤ 0 / 越幅度上界)
		/// </summary>
		InvalidItem = 2,
		/// <summary>
		/// 持有不足(取回时该道具库存为 0)
		/// </summary>
		NotEnough = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 使用道具裁决结果码
	/// </summary>
	public enum UseItemResultCode
	{
		/// <summary>
		/// 成功:已扣道具 + 产出,响应带消耗量 + 产出清单
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号身份(登录失败 / 链路异常)
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 道具 id 无配置行
		/// </summary>
		UnknownItem = 2,
		/// <summary>
		/// 持有 / 未过期批次不足以扣 Count
		/// </summary>
		NotEnough = 3,
		/// <summary>
		/// 目标为限时道具且可用批次均已过期(无未过期批次可扣)
		/// </summary>
		Expired = 4,
		/// <summary>
		/// 道具配置为不可用 / 使用效果本轮不支持(如效果非货币类,或货币指向无对应服务端资源)
		/// </summary>
		NotUsable = 5,
		/// <summary>
		/// 请求序号重复(reqSeq <= 已处理值),幂等丢弃,未二次扣;客户端以推送 / 快照对齐权威背包
		/// </summary>
		Duplicate = 6,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 写入异常 / 乐观并发重试耗尽)
		/// </summary>
		ServiceUnavailable = 7,
		/// <summary>
		/// 参数非法(itemId / count / reqSeq <= 0)
		/// </summary>
		InvalidRequest = 8
	}

	/// <summary>
	/// 领取奖励裁决结果码（§3.4）
	/// </summary>
	public enum MailClaimResultCode
	{
		/// <summary>
		/// 成功并已授权发奖（响应附奖励列表；库 id 未登记时奖励列表为空但仍 Success，见 SV6）
		/// </summary>
		Success = 0,
		/// <summary>
		/// 按邮件标识 + 会话账号定位不到这封邮件（不崩，以结果码回包）
		/// </summary>
		MailNotFound = 1,
		/// <summary>
		/// 该邮件无附件（附件库 id = 0，纯通知邮件）；不发奖
		/// </summary>
		NoReward = 2,
		/// <summary>
		/// 本账号已领过此邮件（响应不附奖励列表）
		/// </summary>
		AlreadyClaimed = 3,
		/// <summary>
		/// 邮件有有效期且服务端时钟已超过（过期判定用服务端时钟）
		/// </summary>
		Expired = 4,
		/// <summary>
		/// 服务不可用（无法建立身份 / 无法处理）；客户端提示重试、邮件保持可领、不本地放行（§四）
		/// </summary>
		ServiceUnavailable = 5
	}

	/// <summary>
	/// 邮件发件人类型（服务端赋值；客户端可按类型配图标/名称）
	/// </summary>
	public enum MailSenderType
	{
		/// <summary>
		/// 系统邮件（运营广播 / GM 默认）
		/// </summary>
		System = 0,
		/// <summary>
		/// 排行榜奖励（结算 / 每日 / 点赞）
		/// </summary>
		RankReward = 1
	}

	/// <summary>
	/// 交付订单裁决结果码
	/// </summary>
	public enum DeliverOrderResultCode
	{
		/// <summary>
		/// 成功:服务端已发奖、回带最新快照 + 权威绝对余额(发起方据此对账),对其它会话推 delta(排除发起方)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 槽位越界 / 该槽空(未刷或已交付被空槽占位)
		/// </summary>
		InvalidSlot = 2,
		/// <summary>
		/// 本轮该槽已交付(DeliveredMask 已置)
		/// </summary>
		AlreadyDelivered = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / ChangeProperty 失败
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 属性类型(P3 十类:钻石 + 五类玩法货币/体力 + 五种元层进度计数器,服务端权威化,原云存档 blob 迁出第 1 批)
	/// </summary>
	public enum PropertyType
	{
		/// <summary>
		/// 钻石
		/// </summary>
		Diamond = 0,
		/// <summary>
		/// 灵力(玩法软货币,纯增减计数器)
		/// </summary>
		SoulPower = 1,
		/// <summary>
		/// 虔诚币(长期主线货币,纯增减计数器)
		/// </summary>
		Piety = 2,
		/// <summary>
		/// 守护者累积经验(玩法侧第四种货币,纯增减计数器;与 PlayerDoc.Level/Exp 玩家账号经验不复用)
		/// </summary>
		GuardianExp = 3,
		/// <summary>
		/// 玩法体力(带离线随时间恢复;服务端按 EnergyLastRecoverMs + EnergyRecoverIntervalMs 懒结算)
		/// </summary>
		Energy = 4,
		/// <summary>
		/// 女神评级(玩法产出,单调递增)
		/// </summary>
		GoddessRating = 5,
		/// <summary>
		/// 章节解锁数(玩法产出,单调递增)
		/// </summary>
		UnlockedChapter = 6,
		/// <summary>
		/// 盲盒计数(玩法产出;可增可减 —— 攒盒 +、开盒 -)
		/// </summary>
		BlindBoxCount = 7,
		/// <summary>
		/// 神庙修缮计数(动作产出,修缮动作触发,单调递增)
		/// </summary>
		TempleRepaired = 8,
		/// <summary>
		/// 神庙修缮游标(动作产出,指向下一个待修缮项,单调递增)
		/// </summary>
		NextRepairIndex = 9
	}

	/// <summary>
	/// 属性变更裁决结果码(§3.3.2 + §3.4)
	/// </summary>
	public enum PropertyChangeResultCode
	{
		/// <summary>
		/// 成功并已写库 + 推送(响应附变更后该属性余额)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号身份(35 登录失败 / 链路异常)→ 客户端段下一刀重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 类型枚举未知(协议非法)
		/// </summary>
		UnknownType = 2,
		/// <summary>
		/// delta 范围非法(超出 [-类型上界, +类型上界],防 Int 极值绕过校验)
		/// </summary>
		InvalidRequest = 3,
		/// <summary>
		/// 余额不足(当前 + delta < 0,响应 NewAmount = 当前实际余额便于客户端 toast)
		/// </summary>
		NotEnough = 4,
		/// <summary>
		/// 类型上界溢出(当前 + delta > 上界,响应 NewAmount = 当前实际余额)
		/// </summary>
		OverLimit = 5,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 写入异常)→ 客户端段下一刀提示重试,变更未生效
		/// </summary>
		ServiceUnavailable = 6
	}

	/// <summary>
	/// 查询 ledger 流水裁决结果码(§3.3)
	/// </summary>
	public enum AttrLedgerQueryResultCode
	{
		/// <summary>
		/// 查询成功(含返空数组的成功:limit=0 / 账号无 ledger / 过滤后无匹配)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 参数非法(kind 整数未知 / sinceTs 负数 / limit 负数);limit 超 100 钳制不报错
		/// </summary>
		InvalidRequest = 1,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 查询抛 Mongo 异常)
		/// </summary>
		ServiceUnavailable = 2
	}

	/// <summary>
	/// 设置玩家档案状态裁决结果码
	/// </summary>
	public enum SetProfileStateResultCode
	{
		/// <summary>
		/// 成功:三态已原子 $set;响应回带服务端当前权威三态
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// sanity 拒(SkinMono 非 0/1,或 SkinMonoId / TempleDecorated 落合法段外)
		/// </summary>
		InvalidRequest = 2,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常
		/// </summary>
		ServiceUnavailable = 3
	}

	/// <summary>
	/// 上报成绩裁决结果码（§3.2）
	/// </summary>
	public enum RankSubmitResultCode
	{
		/// <summary>
		/// 够入榜要求且高于已存最佳 → 服务端已更新为新最佳（响应 BestScore = 本次成绩）
		/// </summary>
		BestRefreshed = 0,
		/// <summary>
		/// 够入榜要求但不高于已存最佳 → 接受请求但不更新（响应 BestScore = 已存最佳）
		/// </summary>
		BestNotRefreshed = 1,
		/// <summary>
		/// 低于该榜入榜要求 → 不进榜、不写存储（响应 BestScore = 已存最佳，无成绩则为 0）
		/// </summary>
		BelowEnterRequirement = 2,
		/// <summary>
		/// 榜 id 在服务端榜配置查不到（不崩，以结果码回包）
		/// </summary>
		RankNotFound = 3,
		/// <summary>
		/// 服务不可用（无法建立身份 / 无法处理）；客户端不阻断玩法、稍后重连可重报（§四）
		/// </summary>
		ServiceUnavailable = 4,
		/// <summary>
		/// 服务端反作弊拦截（超绝对上限 / 频率过密 / 跃升异常等）→ 不进榜、不写存储；响应 BestScore = 已存最佳
		/// </summary>
		RejectedByAntiCheat = 5
	}

	/// <summary>
	/// 查榜结果码（§3.5）
	/// </summary>
	public enum RankQueryResultCode
	{
		/// <summary>
		/// 查询成功（条目列表 + 我的名次 + 我的分数有效）
		/// </summary>
		Success = 0,
		/// <summary>
		/// 榜 id 在服务端榜配置查不到
		/// </summary>
		RankNotFound = 1,
		/// <summary>
		/// 服务不可用；客户端回退设计 22 本地源、不伪造全服名次（§四）
		/// </summary>
		ServiceUnavailable = 2
	}

	/// <summary>
	/// 领取每日/点赞奖裁决结果码
	/// </summary>
	public enum RankClaimResultCode
	{
		/// <summary>
		/// 成功：服务端已投奖励邮件到收件箱、写今日已领标记（奖励在邮件领取时到账）
		/// </summary>
		Success = 0,
		/// <summary>
		/// 未入榜（每日奖要求在榜；名次 0 = 无成绩/低于入榜要求/超入榜上限）→ 不发
		/// </summary>
		NotRanked = 1,
		/// <summary>
		/// 无该类奖励（该名次档无每日奖 / 该榜无点赞奖 / 榜 id 查不到 / ClaimType 非法）→ 不发
		/// </summary>
		NoReward = 2,
		/// <summary>
		/// 今日已领过该类奖（服务端跨天重置，明日可再领）→ 不重复发
		/// </summary>
		AlreadyClaimedToday = 3,
		/// <summary>
		/// 服务不可用（会话未登录 / 组件或 MongoDB 未就绪）；客户端提示重试、不本地放行
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 兑换裁决结果码
	/// </summary>
	public enum RedeemResultCode
	{
		/// <summary>
		/// 成功并已授权发奖（响应附奖励列表）
		/// </summary>
		Success = 0,
		/// <summary>
		/// 码不存在或规整后为空
		/// </summary>
		InvalidCode = 1,
		/// <summary>
		/// 本账号已兑过此码（响应不附奖励列表）
		/// </summary>
		AlreadyRedeemed = 2,
		/// <summary>
		/// 码已过期（服务端时钟判定）
		/// </summary>
		Expired = 3,
		/// <summary>
		/// 全局限量已满
		/// </summary>
		LimitReached = 4,
		/// <summary>
		/// 服务不可用（无法建立身份 / 无法处理），客户端应提示重试、码保持可兑
		/// </summary>
		ServiceUnavailable = 5
	}

	/// <summary>
	/// 改名裁决结果码
	/// </summary>
	public enum RenameResultCode
	{
		/// <summary>
		/// 成功:昵称已写、RenameCount+1、(若扣费)钻石已扣;响应回带最新值供客户端对账
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 昵称非法(空串 / 全空白 / 超长;服务端基本 sanity 校验未过)
		/// </summary>
		InvalidName = 2,
		/// <summary>
		/// 钻石不足(改名费扣减失败),昵称未改
		/// </summary>
		NotEnoughDiamond = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常,昵称未改
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 塔罗牌购买进度裁决结果码
	/// </summary>
	public enum TarotPurchaseResultCode
	{
		/// <summary>
		/// 成功:已扣虔诚币、进度 +1,回带权威虔诚币余额 + 该牌新进度 + 进度全集
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号 → 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 牌 id 不在 TbTarotCard 表(或该牌 UnlockCosts 未配置)
		/// </summary>
		UnknownCard = 2,
		/// <summary>
		/// 该牌进度已满(已激活,幂等拒绝,不扣币)
		/// </summary>
		AlreadyMaxed = 3,
		/// <summary>
		/// 虔诚币不足(CAS 过滤未命中且重读确认不足)
		/// </summary>
		NotEnoughPiety = 4,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 配置缺失
		/// </summary>
		ServiceUnavailable = 5
	}

	/// <summary>
	/// 错误码枚举
	/// </summary>
	public enum ErrorCodeEnum
	{
		/// <summary>
		/// 成功
		/// </summary>
		Success = 0,
		/// <summary>
		/// 失败
		/// </summary>
		Failed = 1,
		/// <summary>
		/// 未知错误
		/// </summary>
		UnknownError = 2,
		/// <summary>
		/// 参数错误
		/// </summary>
		InvalidParameter = 100,
		/// <summary>
		/// 权限不足
		/// </summary>
		PermissionDenied = 101
	}

	/// <summary>
	/// 玩家状态
	/// </summary>
	public enum PlayerState
	{
		Offline = 0,
		Online = 1,
		Busy = 2,
		Away = 3
	}

	/// <summary>
	/// 祈愿裁决结果码
	/// </summary>
	public enum WishForEnergyResultCode
	{
		/// <summary>
		/// 成功:灵力已扣、体力已发(夹软上限)、WishUsedToday+1;响应回带最新值供客户端对账
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(35 登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 今日祈愿次数已达上限(懒重置后仍 >= WishDailyLimit),不扣不发
		/// </summary>
		DailyLimitReached = 2,
		/// <summary>
		/// 灵力不足(扣灵力失败),不发体力
		/// </summary>
		NotEnoughSoul = 3,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常
		/// </summary>
		ServiceUnavailable = 4
	}

	/// <summary>
	/// 取用溢出储存裁决结果码
	/// </summary>
	public enum WithdrawOverflowResultCode
	{
		/// <summary>
		/// 成功:池已转入体力(实际取用量 = min(池, 硬顶-体力));响应回带取用后体力 + 池余
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号(登录链路异常)→ 客户端重登
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 无可取:池为 0,或体力已达存储硬顶转不进(未动数据)
		/// </summary>
		Empty = 2,
		/// <summary>
		/// MongoDB 不可达 / 服务未就绪 / 写库异常,未生效
		/// </summary>
		ServiceUnavailable = 3
	}

	/// <summary>
	/// 清档裁决结果码
	/// </summary>
	public enum ClearPlayerDataResultCode
	{
		/// <summary>
		/// 清档成功(玩家文档已重置为默认新手态、在局对局文档已删除或本就不存在)。幂等:重复清同样返 Success。
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号身份(35 登录失败 / 链路异常 / Account.PlayerId 空)。客户端段重登。
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 写入异常)。变更未生效(或部分生效),客户端段提示重试。
		/// </summary>
		ServiceUnavailable = 2
	}

	/// <summary>
	/// GM 清盘裁决结果码
	/// </summary>
	public enum ClearBoardResultCode
	{
		/// <summary>
		/// 清盘成功(盘面已清空,Step 已推进)
		/// </summary>
		Cleared = 0,
		/// <summary>
		/// 对局不存在(gameId 在会话上查无此局)
		/// </summary>
		GameNotFound = 1,
		/// <summary>
		/// 会话未登录(无法确定身份)
		/// </summary>
		NotLoggedIn = 2
	}

	/// <summary>
	/// GM 清背包裁决结果码
	/// </summary>
	public enum ClearInventoryResultCode
	{
		/// <summary>
		/// 清背包成功(两轨已清空;幂等:重复清同样返 Success)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号身份(未登录 / 链路异常)
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 写入异常)
		/// </summary>
		ServiceUnavailable = 2
	}

	/// <summary>
	/// GM 发测试道具裁决结果码
	/// </summary>
	public enum GrantTestItemResultCode
	{
		/// <summary>
		/// 发放成功(已入背包并推送整份快照)
		/// </summary>
		Success = 0,
		/// <summary>
		/// 会话未挂账号身份(未登录 / 链路异常)
		/// </summary>
		NotLoggedIn = 1,
		/// <summary>
		/// 服务不可用(MongoDB 不可达 / 写入异常)
		/// </summary>
		ServiceUnavailable = 2,
		/// <summary>
		/// 道具 id 无 TbItemDef 配置
		/// </summary>
		UnknownItem = 3,
		/// <summary>
		/// 入参非法(itemId <= 0 / count <= 0 / count 超上限)
		/// </summary>
		InvalidRequest = 4
	}


}