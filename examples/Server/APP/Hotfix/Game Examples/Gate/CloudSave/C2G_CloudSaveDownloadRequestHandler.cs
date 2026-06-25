using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 云存档下载入口(Outer RPC,运行在 Gate Scene)。
/// 身份从会话取(playerId);请求无字段(C2G_CloudSaveDownloadRequest 空消息)。
/// 无存档(全新账号首次同步前)返 NoSnapshot——非异常,客户端按"无云存档"路径走本地默认初值。
/// </summary>
public sealed class C2G_CloudSaveDownloadRequestHandler
    : MessageRPC<C2G_CloudSaveDownloadRequest, G2C_CloudSaveDownloadResponse>
{
    protected override async FTask Run(Session session, C2G_CloudSaveDownloadRequest request,
        G2C_CloudSaveDownloadResponse response, Action reply)
    {
        var playerId = GetSessionPlayerId(session);
        if (string.IsNullOrEmpty(playerId))
        {
            Log.Warning("收到 CloudSaveDownload 但会话未登录(无 GateAccountFlagComponent/Account/PlayerId),无法确定身份。");
            response.ResultCode = CloudSaveDownloadResultCode.NotLoggedIn;
            response.ServerBlob = Array.Empty<byte>();
            return;
        }

        var service = session.Scene.GetComponent<CloudSaveServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 CloudSaveServiceComponent,无法处理云存档下载。");
            response.ResultCode = CloudSaveDownloadResultCode.ServiceUnavailable;
            response.ServerBlob = Array.Empty<byte>();
            return;
        }

        var (resultCode, serverVersion, serverBlob) = await CloudSaveServiceHelper.Download(service, playerId);
        response.ResultCode = resultCode;
        response.ServerVersion = serverVersion;
        response.ServerBlob = serverBlob;

        Log.Debug($"CloudSave 下载 playerId={playerId} result={resultCode} serverVersion={serverVersion} size={serverBlob?.Length ?? 0}");
    }

    private static string? GetSessionPlayerId(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null) return null;
        Account account = flag.Account;
        return account?.PlayerId;
    }
}
