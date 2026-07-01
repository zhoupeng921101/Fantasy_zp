using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 批量属性变更请求入口(Outer RPC,运行在 Gate Scene)。
/// 传输层聚合:客户端一次玩法事件同时改多个属性时,把 N 条 C2G_PropertyChangeRequest 收敛成 1 条 RPC 减少往返;
/// 语义仍是逐项独立裁决,非全或无 —— 服务端对每一项调用与单条链路同一套
/// PlayerPropertyServiceHelper.ChangeProperty(serverAuthoritative:false,复用限界信任 + 频率闸 + 夹界 + 推送),
/// 逐项收集结果回包,一项失败(NotEnough / OverLimit / ServiceUnavailable)不回滚也不阻断其它项(奖励互相独立)。
///
/// 身份从会话取(同单条链路 SV6/SV7 + SV17 红线):读会话上 GateAccountFlagComponent → Account.Name(= UUID),
/// 请求只携带类型 + delta + 批次 reason,**不接受**客户端自报账号 / 绝对余额。会话未登录 → 全批空回(Results 保持空)。
///
/// 每项落账 reason = "{批次 Reason}_{Type}",与单条链路 reason 口径一致(供 ledger / 日志辨识具体项)。
/// 每个 PropertyType 在一个 batch 内约定最多出现一次,同 account|type 100ms 频率闸在 batch 内自然不误触。
/// Results 顺序与请求 Items 一一对应;Items 空 → Results 空(no-op 合法回)。所有结果以逐项 ResultCode 回包,不抛异常断连,框架 RPC ErrorCode 始终 0。
/// </summary>
public sealed class C2G_PropertyBatchChangeRequestHandler : MessageRPC<C2G_PropertyBatchChangeRequest, G2C_PropertyBatchChangeResponse>
{
    protected override async FTask Run(Session session, C2G_PropertyBatchChangeRequest request,
        G2C_PropertyBatchChangeResponse response, Action reply)
    {
        // 身份从会话取(非请求参数,SV17 红线 ②)。未登录 = 35 链路未走完 → 全批不处理,Results 保持空。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 PropertyBatchChangeRequest 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份,全批空回。");
            return;
        }

        // Items 空(客户端无净变更时理论上不发本 RPC,防御性)→ Results 空,no-op 合法回。
        if (request.Items == null || request.Items.Count == 0)
        {
            return;
        }

        var batchReason = request.Reason ?? string.Empty;

        // 逐项独立:每项调与单条链路同一套 ChangeProperty(serverAuthoritative:false),各自记 result,互不阻断。
        foreach (var item in request.Items)
        {
            // 每项落账 reason 携带 Type 后缀,与单条链路口径一致,便于 ledger / 日志辨识具体项。
            var itemReason = string.Concat(batchReason, "_", item.Type.ToString());
            var (resultCode, newAmount) = await PlayerPropertyServiceHelper.ChangeProperty(
                session.Scene, account, item.Type, item.Delta, itemReason);

            var resultItem = PropertyBatchChangeResultItem.Create();
            resultItem.Type = item.Type;
            resultItem.ResultCode = resultCode;
            resultItem.NewAmount = newAmount;
            response.Results.Add(resultItem);

            // 逐项成功即起推送,但**排除发起会话**:请求方已从本 batch 响应拿到全部逐项权威值,自推冗余;
            // 只推该账号其它在线会话对齐(单会话模型下发起方即唯一会话 → 不推)。失败项不推送(同单条链路 SV8/SV9)。
            if (resultCode == PropertyChangeResultCode.Success)
            {
                PlayerPropertyServiceHelper.SendDeltaPushToExcept(session.Scene, account, session, item.Type, newAmount, itemReason);
            }
        }
    }

    /// <summary>从会话取登录时绑定的账号名(同单条 PropertyChange handler 范式)。无登录标记返回 null。</summary>
    private static string? GetSessionAccountName(Session session)
    {
        var flag = session.GetComponent<GateAccountFlagComponent>();
        if (flag == null)
        {
            return null;
        }

        Account account = flag.Account;
        return account?.Name;
    }
}
