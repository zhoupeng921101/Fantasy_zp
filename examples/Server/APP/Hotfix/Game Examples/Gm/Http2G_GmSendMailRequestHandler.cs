using System;
using Fantasy.Async;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// GM 发邮件(Inner RPC,HttpGift 场景转发到 Gate 场景执行)。复用 MailDecisionHelper.SendMailTo 往定向邮件集合投一封带奖励的邮件,
/// 玩家下次拉邮件列表可见、可领(邮件为拉取制,无即时推送)。
/// Response.Code:0=成功(附 MailId);非0=失败(-1 空账号 / -2 邮件组件缺失 / -3 服务不可用)。
/// </summary>
public sealed class Http2G_GmSendMailRequestHandler
    : AddressRPC<Scene, Http2G_GmSendMailRequest, G2Http_GmSendMailResponse>
{
    protected override async FTask Run(Scene scene, Http2G_GmSendMailRequest request,
        G2Http_GmSendMailResponse response, Action reply)
    {
        if (string.IsNullOrEmpty(request.AccountId))
        {
            response.Code = -1;
            response.Message = "empty accountId";
            return;
        }
        var mailService = scene.GetComponent<MailServiceComponent>();
        if (mailService == null)
        {
            response.Code = -2;
            response.Message = "mail service unavailable";
            return;
        }

        var mailId = await MailDecisionHelper.SendMailTo(mailService, request.AccountId,
            request.SenderTextId, request.TitleTextId, request.ContentTextId, request.ExpireDays, request.RewardId);
        if (string.IsNullOrEmpty(mailId))
        {
            response.Code = -3;
            response.Message = "mail service unavailable (mongo not ready)";
            Log.Warning($"GM 发邮件失败(服务不可用) account={request.AccountId} reward={request.RewardId}");
            return;
        }

        response.Code = 0;
        response.Message = "ok";
        response.MailId = mailId;
        Log.Info($"GM 发邮件成功 account={request.AccountId} reward={request.RewardId} mailId={mailId}");
    }
}
