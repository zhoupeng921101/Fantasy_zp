using System;
using Fantasy.Async;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 改货币(Inner RPC,HttpGift 场景转发到 Gate 场景执行)。走服务端权威高信任通道(serverAuthoritative=true,
/// 跳过客户端限速与单笔小额闸——GM 金额由服务端算定、非客户端上报),GM 侧另设单次幅度硬上限防调试误填天量。
/// 复用 PlayerPropertyServiceHelper.ChangeProperty,成功后推该属性余额到在线会话。
/// Response.Code:0=成功;非0=PropertyChangeResultCode 值或 GM 侧校验失败(-1 空账号 / -2 幅度越界 / -3 未知类型)。
/// </summary>
public sealed class Http2G_GmChangeCurrencyRequestHandler
    : AddressRPC<Scene, Http2G_GmChangeCurrencyRequest, G2Http_GmChangeCurrencyResponse>
{
    /// <summary>GM 单次货币变更幅度硬上限(高信任通道无框架单笔闸,自设上限防调试误填天量)。</summary>
    private const long GmMaxSingleDelta = 1_000_000L;

    protected override async FTask Run(Scene scene, Http2G_GmChangeCurrencyRequest request,
        G2Http_GmChangeCurrencyResponse response, Action reply)
    {
        if (string.IsNullOrEmpty(request.AccountId))
        {
            response.Code = -1;
            response.Message = "empty accountId";
            return;
        }
        if (request.Delta == 0 || request.Delta > GmMaxSingleDelta || request.Delta < -GmMaxSingleDelta)
        {
            response.Code = -2;
            response.Message = $"delta out of gm range (0 < |delta| <= {GmMaxSingleDelta})";
            return;
        }

        // 未知货币类型不在此预判:未知 int 强转 PropertyType 合法(不抛),ChangeProperty 内 TryGetTypeMeta 会返 UnknownType,
        // 类型合法性单源于 helper(避免此处与 helper 各列一份枚举、增删成员时漂移)。
        var reason = string.IsNullOrEmpty(request.Reason) ? "gm_change_currency" : request.Reason;
        var (resultCode, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
            scene, request.AccountId, (PropertyType)request.Type, request.Delta, reason, serverAuthoritative: true);
        response.Code = (int)resultCode;
        response.Message = resultCode.ToString();
        response.NewAmount = newAmount;

        if (resultCode == PropertyChangeResultCode.Success)
        {
            PlayerPropertyServiceHelper.SendDeltaPushTo(scene, request.AccountId, (PropertyType)request.Type, newAmount, reason);
            Log.Info($"GM 改货币成功 account={request.AccountId} type={request.Type} delta={request.Delta} newAmount={newAmount}");
        }
        else
        {
            Log.Warning($"GM 改货币未成功 account={request.AccountId} type={request.Type} delta={request.Delta} code={resultCode}");
        }
    }
}
