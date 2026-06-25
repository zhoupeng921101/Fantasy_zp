using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Network.Roaming;

namespace Fantasy;

public sealed class C2G_LoginGameRequestHandler : MessageRPC<C2G_LoginGameRequest,G2C_LoginGameResponse>
{
    protected override async FTask Run(Session session, C2G_LoginGameRequest request, G2C_LoginGameResponse response, Action reply)
    {
        var accountName = request.AccountName;

        if (string.IsNullOrEmpty(accountName))
        {
            // 懒的写错误码，所以只要错误码不是0就是出错了。
            // 想细化各种情况的可以自己加错误码。
            // 其实应该是要用配置表来做一个错误码列表用于前后端查询错误码使用的。
            // 这里只解释一次，后面有错误码的直接使用不会再加注释了。
            response.ErrorCode = 1;
            return;
        }

        // 账号账本 upsert(设计 35 §3.2):必须在挂会话身份之前。
        // 单条原子 upsert:不存在则 insert 首次注册时间 / 末次登录时间 / 状态=0(首连自动注册);
        // 存在则仅 update 末次登录时间(重连)。失败 → 返登录失败,短路后续(不挂会话身份)。
        var accountUpsertErrorCode = await AccountServiceHelper.RegisterOrLogin(session.Scene, accountName);
        if (accountUpsertErrorCode != 0)
        {
            response.ErrorCode = accountUpsertErrorCode;
            return;
        }

        // 玩家数据 setOnInsert + 读整份文档(设计 37 §3.2 处理顺序步骤 4):
        // 首登 → insert 三属性初始值 + 档案初值(昵称/等级/经验);重登 → update 路径不动既有值、读当前文档。
        // 失败 → 返登录失败,短路后续(不挂会话身份,沿 35 + 30 「服务不可用不本地放行」基线)。
        var (propErrorCode, playerDoc) = await PlayerPropertyServiceHelper.InitOrLoad(session.Scene, accountName);
        if (propErrorCode != 0)
        {
            response.ErrorCode = propErrorCode;
            return;
        }

        // playerId 签发/认领(P0 身份收敛):账号尚无 playerId 时,客户端 localPlayerId 非空 → 认领、空 → 服务端新生成;
        // 账号已有则忽略客户端上传值并回带服务端权威值。结果填进 response.PlayerId 让客户端落到 PlayerPrefs。
        // 失败 → 登录失败短路(不挂会话身份,沿 35 / 37 服务不可用基线)。
        var (idErrorCode, playerId) = await PlayerPropertyServiceHelper.ClaimOrIssuePlayerId(
            session.Scene, accountName, playerDoc, request.LocalPlayerId ?? string.Empty);
        if (idErrorCode != 0)
        {
            response.ErrorCode = idErrorCode;
            return;
        }
        response.PlayerId = playerId;

        if (!AccountManageHelper.Add(session.Scene, accountName, out var account))
        {
            response.ErrorCode = 1;
            return;
        }
        // var account = Entity.Create<Account>(session.Scene);
        account.Session = session;
        // 把签发/认领得到的 playerId 一并挂到会话级 Account 实体上,后续 handler(如 P3 云存档)直接读
        // flag.Account.PlayerId,不必为每次请求回库读 PlayerDoc(身份取用 fast path)。
        account.PlayerId = playerId;
        // 挂载组件用来标记这个Session下的Account，后面下线流程也会用到
        session.AddComponent<GateAccountFlagComponent>().Account = account;
        // 执行上线流程
        await AccountHelper.Online(session, account);

        // 上线流程完成后,下发玩家信息整份快照到该会话(取代原 G2C_PropertyInitSnapshot)。
        // 一条 G2C_PlayerInfoSnapshot 携带基础档案(昵称/等级/经验)+ 三数值属性余额,客户端 Player 模块作初视图。
        // 放 Online 之后:确保 GateAccountFlagComponent + account.Session 都已挂全,推送通路稳。
        PlayerPropertyServiceHelper.SendPlayerInfoTo(session, accountName, playerDoc);

        // P2 Phase 1·normal 订单:登录拉订单快照(先跑刷新结算 → 派生快照 → 推送)。
        // 旧文档(P1 期玩家)缺三字段(OrderCursor / LastOrderRefreshMs / OrderDeliveredMask)→ BSON 默认 0/0L,
        // ApplyOrderRefreshIfDue 会走 bootstrap 分支(LastOrderRefreshMs==0 → 写 nowMs、本次不刷),首登/旧档同口径。
        // 失败(MongoDB 抖动 / 服务未挂)→ Helper 内 Warning 不抛、不阻断登录链路,客户端拿到的快照可能是空 ActiveOrders 列表,
        // 客户端段降级显示(无订单可交付)直到下次拉。
        var propService = session.Scene.GetComponent<PlayerPropertyServiceComponent>();
        // playerDoc 上面 propErrorCode == 0 分支已保证非 null(InitOrLoad 成功返回的 doc)。
        if (propService != null && playerDoc != null)
        {
            await MergeOrderServiceHelper.ApplyOrderRefreshIfDue(propService, accountName, playerDoc, Fantasy.Helper.TimeHelper.Now);
            var orderSnapshot = MergeOrderServiceHelper.BuildSnapshot(playerDoc);
            MergeOrderServiceHelper.SendSnapshotTo(session, orderSnapshot);
        }

        // 活动系统登录触发(设计 39 §3.5 Login 类节律):遍历 Type=Login 活动各自 counter+1 + 判达标 + 抢占 + 发邮件。
        // 不写入 response、不影响 G2C_LoginGameResponse 契约(零客户端协议改);
        // MongoDB / 活动配置 / Mail 服务任一未就绪都静默跳过、不抛、不影响登录链路(设计 39 §四 + §3.4)。
        // 协程化:不阻塞登录响应返回(响应在 Run 协程退出时由框架自动 reply,这里挂出后台协程继续跑活动判定)。
        var activitySvc = session.Scene.GetComponent<ActivityServiceComponent>();
        var mailSvc = session.Scene.GetComponent<MailServiceComponent>();
        ActivityEvalHelper.OnLogin(activitySvc, mailSvc, accountName, Fantasy.Helper.TimeHelper.Now).Coroutine();
    }
}