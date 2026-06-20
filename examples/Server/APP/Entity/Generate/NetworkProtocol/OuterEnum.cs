// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy
{
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
	/// 属性类型(本子单三类)
	/// </summary>
	public enum PropertyType
	{
		/// <summary>
		/// 金币
		/// </summary>
		Coin = 0,
		/// <summary>
		/// 钻石
		/// </summary>
		Diamond = 1,
		/// <summary>
		/// 体力
		/// </summary>
		Stamina = 2
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
		ServiceUnavailable = 4
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


}