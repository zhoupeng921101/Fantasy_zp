using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Network.Interface;
using GameConfig.reward;

namespace Fantasy;

/// <summary>
/// GM 发邮件(Inner RPC,HttpGift 场景转发到 Gate 场景执行)。复用 MailDecisionHelper.SendMailTo 往定向邮件集合投一封带奖励的邮件,
/// 玩家下次拉邮件列表可见、可领(邮件为拉取制,无即时推送)。
/// 奖励以紧凑字符串传入(格式 `Currency,4,5|Item,30001,2`):| 分隔条目、逗号分隔 类型/目标id/数量,类型接受 Currency/Item(不区分大小写);
/// 空串 = 无奖励通知邮件。解析非法即返错误码不投递;每条数量有防呆上限(见 MaxRewardAmount),条目数有上限(见 MaxRewardEntries)。
/// Response.Code:0=成功(附 MailId);非0=失败(-1 空账号 / -2 邮件组件缺失 / -3 服务不可用 / -4 奖励字符串非法)。
/// </summary>
public sealed class Http2G_GmSendMailRequestHandler
    : AddressRPC<Scene, Http2G_GmSendMailRequest, G2Http_GmSendMailResponse>
{
    /// <summary>GM 单条奖励数量防呆上限(挡住误填 / 溢出;正常运营发奖远小于此)。</summary>
    private const int MaxRewardAmount = 100_000_000;

    /// <summary>GM 单封邮件奖励条目数防呆上限。</summary>
    private const int MaxRewardEntries = 64;

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

        if (!TryParseRewards(request.Rewards, out var rewards, out var parseError))
        {
            response.Code = -4;
            response.Message = $"invalid rewards: {parseError}";
            Log.Warning($"GM 发邮件奖励解析失败 account={request.AccountId} rewards='{request.Rewards}' err={parseError}");
            return;
        }

        var mailId = await MailDecisionHelper.SendMailTo(mailService, request.AccountId,
            request.SenderTextId, request.TitleTextId, request.ContentTextId, request.ExpireDays, rewards);
        if (string.IsNullOrEmpty(mailId))
        {
            response.Code = -3;
            response.Message = "mail service unavailable (mongo not ready)";
            Log.Warning($"GM 发邮件失败(服务不可用) account={request.AccountId} rewardCount={rewards.Count}");
            return;
        }

        response.Code = 0;
        response.Message = "ok";
        response.MailId = mailId;
        Log.Info($"GM 发邮件成功 account={request.AccountId} rewardCount={rewards.Count} mailId={mailId}");
    }

    /// <summary>
    /// 解析 GM 奖励紧凑字符串为内联奖励条目列表。格式 `Currency,4,5|Item,30001,2`;空串 / 全空白 → 空列表(无奖励)。
    /// 任一条目格式 / 类型 / 目标 id / 数量非法,或条目数 / 单条数量超上限 → 返 false 并给出 error(整封拒绝,不部分发)。
    /// </summary>
    private static bool TryParseRewards(string? raw, out List<RewardEntryDoc> rewards, out string error)
    {
        rewards = new List<RewardEntryDoc>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true; // 无奖励通知邮件。
        }

        var segments = raw.Split('|');
        if (segments.Length > MaxRewardEntries)
        {
            error = $"奖励条目数 {segments.Length} 超上限 {MaxRewardEntries}";
            return false;
        }

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue; // 容忍空段(如末尾多余的 |)。
            }

            var parts = trimmed.Split(',');
            if (parts.Length != 3)
            {
                error = $"条目格式错误(应为 类型,目标id,数量):'{segment}'";
                return false;
            }

            var typeStr = parts[0].Trim();
            int rewardType;
            if (string.Equals(typeStr, "Currency", StringComparison.OrdinalIgnoreCase))
            {
                rewardType = (int)ERewardType.Currency;
            }
            else if (string.Equals(typeStr, "Item", StringComparison.OrdinalIgnoreCase))
            {
                rewardType = (int)ERewardType.Item;
            }
            else
            {
                error = $"未知奖励类型 '{typeStr}'(应为 Currency / Item)";
                return false;
            }

            if (!int.TryParse(parts[1].Trim(), out var targetId) || targetId <= 0)
            {
                error = $"目标 id 非法 '{parts[1]}'";
                return false;
            }
            if (!int.TryParse(parts[2].Trim(), out var amount) || amount <= 0)
            {
                error = $"数量非法 '{parts[2]}'";
                return false;
            }
            if (amount > MaxRewardAmount)
            {
                error = $"数量 {amount} 超防呆上限 {MaxRewardAmount}";
                return false;
            }

            rewards.Add(new RewardEntryDoc { RewardType = rewardType, TargetId = targetId, Amount = amount });
        }

        return true;
    }
}
