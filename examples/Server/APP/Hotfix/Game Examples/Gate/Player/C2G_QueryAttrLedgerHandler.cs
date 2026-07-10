using System;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

/// <summary>
/// 玩家属性变更流水查询入口(Outer RPC,运行在 Gate Scene)。
/// 玩家身份从会话取(SV11):读会话上登录时挂载的 GateAccountFlagComponent → Account.Name(= UUID),
/// 请求不携带账号字段(协议层即已不预留),即使协议被改坏夹带账号字段、handler 也只信会话身份。
///
/// 入参校验(SV8 / SV10):
///   - kind ∈ {0, 1, 2, 3};其他整数 → InvalidRequest;
///   - sinceTs ≥ 0;负数 → InvalidRequest;
///   - limit < 0 → InvalidRequest;limit > 100 → 钳为 100 但不报错(降级语义,SV5);
///   - limit = 0 → Success + 空列表(SV6,helper 内 short-circuit)。
///
/// 查询委托给 AttrLedgerQueryHelper.QueryAsync(只读 ledger 集合,SV14 + SV18 Code Review)。
///
/// 所有结果以 ResultCode 回包,不抛异常断连(SV12);框架 RPC ErrorCode 始终保持 0。
///
/// 设计基线:design-docs/45-player-attr-ledger-query.md §三 / §四。
/// </summary>
public sealed class C2G_QueryAttrLedgerHandler : MessageRPC<C2G_QueryAttrLedger, G2C_QueryAttrLedgerResponse>
{
    /// <summary>limit 服务端钳制上限(plan O2 默认 100,可改值不破语义)。</summary>
    private const int MaxLimit = 100;

    protected override async FTask Run(Session session, C2G_QueryAttrLedger request,
        G2C_QueryAttrLedgerResponse response, Action reply)
    {
        // 身份从会话取(SV11):非请求参数。会话未登录(无账号标记)= 35 链路未走完 / 会话异常 → ServiceUnavailable
        // (沿 32 邮件 handler 同范式;不引入「NotLoggedIn」结果码,与 plan §3.3 错误码集 3 个保持一致)。
        var account = GetSessionAccountName(session);
        if (string.IsNullOrEmpty(account))
        {
            Log.Warning("收到 QueryAttrLedger 但会话未登录(无 GateAccountFlagComponent/Account),无法确定身份。");
            response.ResultCode = AttrLedgerQueryResultCode.ServiceUnavailable;
            response.HasMore = false;
            return;
        }

        // 入参校验(SV8 + SV10):非法直接返 InvalidRequest,不查 Mongo。
        if (request.SinceTs < 0L || request.Limit < 0)
        {
            response.ResultCode = AttrLedgerQueryResultCode.InvalidRequest;
            response.HasMore = false;
            return;
        }

        // kind = 0 表「不过滤」;1..10 = 各 PropertyType + 1 映射到 PropertyType 枚举;其他整数 → InvalidRequest。
        PropertyType? kindForFilter = null;
        if (request.Kind != 0)
        {
            if (!AttrLedgerQueryHelper.TryMapKindToPropertyType(request.Kind, out var t))
            {
                response.ResultCode = AttrLedgerQueryResultCode.InvalidRequest;
                response.HasMore = false;
                return;
            }
            kindForFilter = t;
        }

        // limit 钳制 [0, MaxLimit](SV5):超上限不报错,降级到上限正常返。
        var clampedLimit = request.Limit > MaxLimit ? MaxLimit : request.Limit;

        var service = session.Scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service == null)
        {
            Log.Error("当前 Scene 下没有 PlayerPropertyServiceComponent,无法查询 ledger。");
            response.ResultCode = AttrLedgerQueryResultCode.ServiceUnavailable;
            response.HasMore = false;
            return;
        }

        var (resultCode, entries, hasMore) = await AttrLedgerQueryHelper.QueryAsync(
            service, account, kindForFilter, request.SinceTs, clampedLimit);

        response.ResultCode = resultCode;
        response.HasMore = hasMore;
        // 生成物 Entries 已初始化为空 List,这里直接 AddRange 即可(避免覆盖列表实例破生成物 Dispose 期望)。
        if (entries.Count > 0)
        {
            response.Entries.AddRange(entries);
        }

        Log.Debug($"QueryAttrLedger account={account} kind={request.Kind} sinceTs={request.SinceTs} " +
                  $"limit={request.Limit}(clamped={clampedLimit}) result={resultCode} count={entries.Count} hasMore={hasMore}");
    }

    /// <summary>从会话取登录时绑定的账号名(同 mail / property handler 范式)。无登录标记返回 null。</summary>
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
