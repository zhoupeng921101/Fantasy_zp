using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 清空玩家数据·重置为新手:客户端按当前会话 playerId 请求把自己的持久数据清掉/重置为默认新手态,
/// 保留账号身份(playerId 不变、账号→playerId 绑定不变),使该号下次登录(或清完重连后重登)
/// 从零开始、与全新玩家一致。
///
/// 身份从会话取(GateAccountFlagComponent.Account):Account.Name = accountId(= PlayerDoc 主键),
/// Account.PlayerId = playerId(= GameSessionDoc 主键)。请求无字段、不接受指定清别人 → 无跨玩家面。
/// 未挂会话身份(组件缺失 / Account 空 / PlayerId 空)→ NotLoggedIn。
///
/// 清的范围 = 该玩家全部 per-player 持久数据(键全部来自会话身份,不接受客户端传参):
///   1. 玩家文档(players,_id=accountId):字段重置为默认新手态(保留 AccountId / PlayerId),
///      走 PlayerPropertyServiceHelper.ResetToNewbie(与首登 setOnInsert 同一组默认值,清后重登逐字段一致)。
///   2. 在局对局文档(block_blast_session,_id=playerId,含盘面/分数/发牌器全态/局内叠加层切片):整条删除,
///      下次进入对局 Load 返 null → 新建(Resumed=false),不复活已清对局。删除经 GameSessionPersistHelper.Delete
///      (对不可达/异常静默吞掉、幂等无档=已删),故此步不因 DB 抖动回 ServiceUnavailable。
///   3. 活动进度(activity_progress,按 Account 删多行):该玩家全部活动 counter / 周期键归零。
///   4. 邮件定向 + 领取记录(mail_directed / mail_record,按 Account 删多行)。
///   5. 排行榜分数(rank_score,按 Account 删多行):退出所有榜。
///   6. 兑换记录(redeem_record,按 Account 删多行):此前已兑码回到可再兑态。
///   7. 属性流水(player_attr_ledger,按 Account 删多行):清档明示例外于「ledger 永不删」不变量(见 helper)。
/// 各步按玩家身份(accountId)从会话取键;由各子系统自己的 ServiceHelper 持有删除(handler 不直伸他人集合)。
/// 任一 DB 失败 / 组件缺失 → ServiceUnavailable(可能已部分生效;客户端段提示重试,各步 DeleteMany 幂等)。
///
/// 刻意不清的全局共享集合(清了会毁所有玩家):accounts(账号身份)、各 *_def / *_template / gift_pool / redeem_code
/// (全局配置)、rank_settle(_id=RankId 全服结算幂等标记)、redeem_counter(_id=Code 全局发放计数)——
/// 后两者虽与玩家行为相关,但无 Account 维度、是全服共享状态,删除会让其他玩家重复结算 / 限量码超发,故不在范围。
///
/// 内存态一致性:内存 Account 只持 Name/Session/PlayerId(均不在清档范围,PlayerId 刻意保留),
/// 货币/订单等持久值只存 MongoDB、无内存缓存,下线流程(AccountHelper.Offline)也不回写库,
/// 故不存在「清库后旧内存值被保存钩子刷回」的竞态。残余窄窗仅为:清档与某并发服务端权威写
/// (订单交付 / 登录活动结算)交错 → 由客户端段「清完强制重连重登」收口(重登从快照重置内存视图)。
///
/// 幂等:重复清同一账号同样把字段刷成默认值 + 删在局对局档(本就无则 DeletedCount=0),结果不变、不报错。
/// </summary>
public sealed class C2G_ClearPlayerDataRequestHandler
    : MessageRPC<C2G_ClearPlayerDataRequest, G2C_ClearPlayerDataResponse>
{
    protected override async FTask Run(Session session, C2G_ClearPlayerDataRequest request,
        G2C_ClearPlayerDataResponse response, Action reply)
    {
        // 会话身份取用:沿 C2G_EnterMainGameRequestHandler 同范式。
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        if (account == null || string.IsNullOrEmpty(account.PlayerId) || string.IsNullOrEmpty(account.Name))
        {
            Log.Warning("收到 ClearPlayerDataRequest 但会话未登录(无 GateAccountFlagComponent/Account/PlayerId),无法确定身份。");
            response.ResultCode = ClearPlayerDataResultCode.NotLoggedIn;
            return;
        }

        var accountName = account.Name;   // = accountId,PlayerDoc 主键
        var playerId = account.PlayerId;  // = GameSessionDoc 主键

        // ── 玩家文档重置 ──────────────────────────────────────────
        var resetErrorCode = await PlayerPropertyServiceHelper.ResetToNewbie(session.Scene, accountName);
        if (resetErrorCode != 0)
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // ── 在局对局文档删除 ──────────────────────────────────────
        // 清档把该玩家在局对局(block_blast_session,_id=playerId,含盘面/分数/发牌器全态/局内叠加层切片)一并删,
        // 使清完重登从零开始(下次进入对局 Load 返 null → 新建 Resumed=false)。
        var gameSessionService = session.Scene.GetComponent<GameSessionServiceComponent>();
        if (gameSessionService == null)
        {
            Log.Error("当前 Scene 下没有 GameSessionServiceComponent,无法删除在局对局档。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // GameSessionPersistHelper.Delete 对不可达 / 异常静默吞掉(Warning 留痕)、幂等:无档等价已删。
        await GameSessionPersistHelper.Delete(gameSessionService, playerId);

        // ── 活动进度删除(activity_progress,按 accountId 删多行) ──
        var activityService = session.Scene.GetComponent<ActivityServiceComponent>();
        if (activityService == null)
        {
            Log.Error("当前 Scene 下没有 ActivityServiceComponent,无法清活动进度。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }
        if (!await ActivityProgressService.ClearByAccount(activityService, accountName))
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // ── 邮件定向 + 领取记录删除(mail_directed / mail_record,按 accountId 删多行) ──
        var mailService = session.Scene.GetComponent<MailServiceComponent>();
        if (mailService == null)
        {
            Log.Error("当前 Scene 下没有 MailServiceComponent,无法清邮件数据。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }
        if (!await MailDecisionHelper.ClearByAccount(mailService, accountName))
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // ── 排行榜分数删除(rank_score,按 accountId 删多行) ──
        var rankService = session.Scene.GetComponent<RankServiceComponent>();
        if (rankService == null)
        {
            Log.Error("当前 Scene 下没有 RankServiceComponent,无法清排行榜分数。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }
        if (!await RankDecisionHelper.ClearByAccount(rankService, accountName))
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // ── 兑换记录删除(redeem_record,按 accountId 删多行) ──
        var redeemService = session.Scene.GetComponent<RedeemServiceComponent>();
        if (redeemService == null)
        {
            Log.Error("当前 Scene 下没有 RedeemServiceComponent,无法清兑换记录。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }
        if (!await RedeemDecisionHelper.ClearByAccount(redeemService, accountName))
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        // ── 属性流水删除(player_attr_ledger,按 accountId 删多行) ──
        var propertyService = session.Scene.GetComponent<PlayerPropertyServiceComponent>();
        if (propertyService == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent,无法清属性流水。");
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }
        if (!await AttrLedgerHelper.ClearByAccount(propertyService, accountName))
        {
            response.ResultCode = ClearPlayerDataResultCode.ServiceUnavailable;
            return;
        }

        response.ResultCode = ClearPlayerDataResultCode.Success;
        Log.Info($"ClearPlayerData 清档成功 account={accountName} playerId={playerId}(玩家文档已重置默认态;在局对局档/活动进度/邮件/排行榜分数/兑换记录/属性流水已删除)。");
    }
}
