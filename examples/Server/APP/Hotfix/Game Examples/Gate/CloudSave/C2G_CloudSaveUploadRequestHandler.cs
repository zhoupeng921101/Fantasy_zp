using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 云存档上传入口(Outer RPC,运行在 Gate Scene)。
/// 身份从会话取(playerId 而非 accountName,云存档按 playerId 寻址):读会话上登录时挂载的
/// GateAccountFlagComponent → Account.PlayerId,请求**不携带** playerId 字段。
/// 上传裁决(blob 上限、version 单调推进、原子写)由 CloudSaveServiceHelper.Upload 统一执行;
/// 所有结果(含失败 / 边界)以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
/// </summary>
public sealed class C2G_CloudSaveUploadRequestHandler
    : MessageRPC<C2G_CloudSaveUploadRequest, G2C_CloudSaveUploadResponse>
{
    protected override async FTask Run(Session session, C2G_CloudSaveUploadRequest request,
        G2C_CloudSaveUploadResponse response, Action reply)
    {
        var playerId = GetSessionPlayerId(session);
        if (string.IsNullOrEmpty(playerId))
        {
            Log.Warning("收到 CloudSaveUpload 但会话未登录(无 GateAccountFlagComponent/Account/PlayerId),无法确定身份。");
            response.ResultCode = CloudSaveUploadResultCode.NotLoggedIn;
            return;
        }

        var service = session.Scene.GetComponent<CloudSaveServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 CloudSaveServiceComponent,无法处理云存档上传。");
            response.ResultCode = CloudSaveUploadResultCode.ServiceUnavailable;
            return;
        }

        var (resultCode, serverVersion, serverBlob) = await CloudSaveServiceHelper.Upload(
            service, playerId, request.Version, request.Blob);
        response.ResultCode = resultCode;
        response.ServerVersion = serverVersion;
        // Stale 时回带服务端当前 blob 供客户端合并;其它分支不带(空数组而非 null,保协议契约稳)。
        response.ServerBlob = serverBlob ?? Array.Empty<byte>();

        Log.Debug($"CloudSave 上传 playerId={playerId} version={request.Version} size={request.Blob?.Length ?? 0} result={resultCode} serverVersion={serverVersion}");
    }

    /// <summary>从会话取登录时绑定的 playerId(云存档寻址锚)。无登录标记 / playerId 未签发返回 null。</summary>
    private static string? GetSessionPlayerId(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.PlayerId;
    }
}
