// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy
{
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