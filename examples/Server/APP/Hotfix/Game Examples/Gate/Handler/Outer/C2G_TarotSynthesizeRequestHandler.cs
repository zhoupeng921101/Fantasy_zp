using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 塔罗牌合成请求入口(Outer RPC,运行在 Gate Scene,塔罗收集系统)。
///
/// 玩家身份从会话取(同 C2G_DeliverOrderRequestHandler):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求只携带牌 id,**不**接受碎片消耗量(服务端按 TbTarotCard 表自算 — 反作弊红线)。
/// 校验 / 原子扣碎片置牌由 TarotCollectionServiceHelper.TrySynthesize 统一执行;
/// 失败以 ResultCode 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
/// </summary>
public sealed class C2G_TarotSynthesizeRequestHandler : MessageRPC<C2G_TarotSynthesizeRequest, G2C_TarotSynthesizeResponse>
{
    protected override async FTask Run(Session session, C2G_TarotSynthesizeRequest request,
        G2C_TarotSynthesizeResponse response, Action reply)
    {
        response.CardId = request.CardId;
        response.FragmentBalance = -1L; // 哨兵:未裁决/未取到权威值,客户端不据此 set

        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        var accountName = account?.Name;
        if (string.IsNullOrEmpty(accountName))
        {
            Log.Warning("收到 TarotSynthesizeRequest 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ResultCode = TarotSynthesizeResultCode.NotLoggedIn;
            return;
        }

        var (resultCode, fragmentItemId, fragmentBalance, collected) =
            await TarotCollectionServiceHelper.TrySynthesize(session.Scene, accountName, request.CardId);

        response.ResultCode = resultCode;
        response.FragmentItemId = fragmentItemId;
        response.FragmentBalance = fragmentBalance;
        // collected == null = 未取到玩家文档(降级路径):CollectedValid=false,客户端保留投影不清空;
        // 非 null(含空列表 = 权威空收集)才可信,客户端整份覆盖。
        response.CollectedValid = collected != null;
        if (collected != null)
        {
            response.CollectedTarotIds.AddRange(collected);
        }
    }
}
