using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 塔罗牌免费推进请求入口(Outer RPC,运行在 Gate Scene,塔罗收集系统)。
///
/// 玩家每次打开主界面(主菜单面板)触发一次:免费(不扣虔诚币)推进当前牌一步。
/// 玩家身份从会话取(同 <see cref="C2G_TarotPurchaseRequestHandler"/>):读 GateAccountFlagComponent → Account.Name = UUID。
/// 请求无字段(当前牌与步数由服务端按 TbTarotCard.DataList 顺位自定,反作弊红线:客户端不上报)。
/// 推进裁决由 <see cref="TarotCollectionServiceHelper.AutoAdvanceFreeStep"/> 统一执行(单条原子 CAS);
/// 失败以 ProgressValid=false 回包,不抛异常断连;框架 RPC ErrorCode 始终保持 0。
/// </summary>
public sealed class C2G_TarotAutoAdvanceRequestHandler
    : MessageRPC<C2G_TarotAutoAdvanceRequest, G2C_TarotAutoAdvanceResponse>
{
    protected override async FTask Run(Session session, C2G_TarotAutoAdvanceRequest request,
        G2C_TarotAutoAdvanceResponse response, Action reply)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        Account? account = flag?.Account;
        var accountName = account?.Name;
        if (string.IsNullOrEmpty(accountName))
        {
            Log.Warning("收到 TarotAutoAdvanceRequest 但会话未登录(无 GateAccountFlagComponent/Account)。");
            response.ProgressValid = false;
            return;
        }

        // progress == null = 读/写库失败(降级路径):ProgressValid=false,客户端保留投影不清空;
        // 非 null(含空字典 = 权威空进度)才可信,客户端整份覆盖。
        var progress = await TarotCollectionServiceHelper.AutoAdvanceFreeStep(session.Scene, accountName);
        response.ProgressValid = progress != null;
        if (progress != null)
        {
            foreach (var kv in progress)
            {
                var entry = TarotProgressEntry.Create();
                entry.CardId = kv.Key;
                entry.Steps = kv.Value;
                response.ProgressAll.Add(entry);
            }
        }
    }
}
