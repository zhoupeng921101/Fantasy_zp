using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 清空玩家数据·重置为新手(客户端 GM 入口):客户端按当前会话身份请求把自己的持久数据清掉 / 重置为默认新手态,
/// 保留账号身份(accountId / playerId 不变),使该号下次登录从零开始、与全新玩家一致。
///
/// 身份从会话取(GateAccountFlagComponent.Account):Account.Name = accountId(= PlayerDoc 主键),
/// Account.PlayerId = playerId(= GameSessionDoc 主键)。请求无字段、不接受指定清别人 → 无跨玩家面。
/// 未挂会话身份(组件缺失 / Account 空 / PlayerId 空)→ NotLoggedIn。
///
/// 清的具体范围、刻意不清的全局集合、幂等性、内存态一致性均见 <see cref="ClearPlayerDataHelper"/>
/// (清号编排与运维 GM 清号 Http2G_GmClearPlayerData 共用同一份,不各写一遍)。
/// 内存态残余窄窗由客户端段「清完强制重连重登」收口(重登从快照重置内存视图)。
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

        response.ResultCode = await ClearPlayerDataHelper.ClearAll(session.Scene, accountName, playerId);
        if (response.ResultCode == ClearPlayerDataResultCode.Success)
        {
            Log.Info($"ClearPlayerData 清档成功 account={accountName} playerId={playerId}(玩家文档已重置默认态;在局对局档/活动进度/邮件/排行榜分数/兑换记录/属性流水已删除)。");
        }
    }
}
