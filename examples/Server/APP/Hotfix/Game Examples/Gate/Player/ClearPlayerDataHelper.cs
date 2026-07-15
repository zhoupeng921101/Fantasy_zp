using Fantasy.Async;

namespace Fantasy;

/// <summary>
/// 清空玩家全部 per-player 持久数据、重置为新手态(保留账号身份:accountId = PlayerDoc 主键、playerId = GameSessionDoc 主键均不变),
/// 使该号下次登录从零开始、与全新玩家一致。客户端 GM 清号(C2G_ClearPlayerData,身份从会话取)与运维 GM 清号
/// (Http2G_GmClearPlayerData,身份从 players 文档取)**共用此编排**,不各写一份。
///
/// 清的范围(键全部为 accountId,除在局对局档用 playerId):
///   1. 玩家文档(players,_id=accountId):字段重置为默认新手态(保留 AccountId / PlayerId),ResetToNewbie(与首登 setOnInsert 同组默认值)。
///   2. 在局对局文档(block_blast_session,_id=playerId,含盘面/分数/发牌器全态/局内叠加层切片):整条删除,下次进入对局 Load 返 null → 新建。
///   3. 活动进度(activity_progress,按 accountId 删多行):全部活动 counter / 周期键归零。
///   4. 邮件定向 + 领取记录(mail_directed / mail_record,按 accountId 删多行)。
///   5. 排行榜分数(rank_score,按 accountId 删多行):退出所有榜。
///   6. 兑换记录(redeem_record,按 accountId 删多行):此前已兑码回到可再兑态。
///   7. 属性流水(player_attr_ledger,按 accountId 删多行):清档明示例外于「ledger 永不删」不变量。
///
/// 刻意不清的全局共享集合(清了会毁所有玩家):accounts(账号身份)、各 *_def / *_template / redeem_code(全局配置)、
/// rank_settle(_id=RankId 全服结算幂等标记)、redeem_counter(_id=Code 全局发放计数)——后两者无 Account 维度、是全服共享状态。
///
/// 任一步 DB 失败 / 组件缺失 → ServiceUnavailable(可能已部分生效;各步 DeleteMany / 重置幂等,调用方可提示重试)。
/// 幂等:重复清同一账号同样把字段刷成默认值 + 删在局对局档(本就无则 DeletedCount=0),结果不变、不报错。
/// </summary>
public static class ClearPlayerDataHelper
{
    /// <summary>执行全量清号。accountId = PlayerDoc 主键;playerId = 在局对局档主键(调用方负责取)。返回结果码。</summary>
    public static async FTask<ClearPlayerDataResultCode> ClearAll(Scene scene, string accountId, string playerId)
    {
        // ── 玩家文档重置 ──
        var resetErrorCode = await PlayerPropertyServiceHelper.ResetToNewbie(scene, accountId);
        if (resetErrorCode != 0)
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        // ── 在局对局文档删除(Delete 对不可达/异常静默吞、幂等无档=已删,不因 DB 抖动回 ServiceUnavailable) ──
        var gameSessionService = scene.GetComponent<GameSessionServiceComponent>();
        if (gameSessionService == null)
        {
            Log.Error("当前 Scene 下没有 GameSessionServiceComponent,无法删除在局对局档。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        await GameSessionPersistHelper.Delete(gameSessionService, playerId);

        // ── 活动进度删除 ──
        var activityService = scene.GetComponent<ActivityServiceComponent>();
        if (activityService == null)
        {
            Log.Error("当前 Scene 下没有 ActivityServiceComponent,无法清活动进度。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        if (!await ActivityProgressService.ClearByAccount(activityService, accountId))
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        // ── 邮件定向 + 领取记录删除 ──
        var mailService = scene.GetComponent<MailServiceComponent>();
        if (mailService == null)
        {
            Log.Error("当前 Scene 下没有 MailServiceComponent,无法清邮件数据。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        if (!await MailDecisionHelper.ClearByAccount(mailService, accountId))
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        // ── 排行榜分数删除 ──
        var rankService = scene.GetComponent<RankServiceComponent>();
        if (rankService == null)
        {
            Log.Error("当前 Scene 下没有 RankServiceComponent,无法清排行榜分数。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        if (!await RankDecisionHelper.ClearByAccount(rankService, accountId))
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        // ── 兑换记录删除 ──
        var redeemService = scene.GetComponent<RedeemServiceComponent>();
        if (redeemService == null)
        {
            Log.Error("当前 Scene 下没有 RedeemServiceComponent,无法清兑换记录。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        if (!await RedeemDecisionHelper.ClearByAccount(redeemService, accountId))
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        // ── 属性流水删除 ──
        var propertyService = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (propertyService == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent,无法清属性流水。");
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }
        if (!await AttrLedgerHelper.ClearByAccount(propertyService, accountId))
        {
            return ClearPlayerDataResultCode.ServiceUnavailable;
        }

        return ClearPlayerDataResultCode.Success;
    }
}
