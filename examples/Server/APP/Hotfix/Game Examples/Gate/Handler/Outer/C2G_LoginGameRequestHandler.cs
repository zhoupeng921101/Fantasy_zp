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

        // 玩家属性账本 setOnInsert + 读快照(设计 37 §3.2 处理顺序步骤 4):
        // 首登 → insert 三属性初始值;重登 → update 路径不动余额、读当前值。
        // 失败 → 返登录失败,短路后续(不挂会话身份,沿 35 + 30 「服务不可用不本地放行」基线)。
        var (propErrorCode, propSnapshot) = await PlayerPropertyServiceHelper.InitOrLoad(session.Scene, accountName);
        if (propErrorCode != 0)
        {
            response.ErrorCode = propErrorCode;
            return;
        }

        if (!AccountManageHelper.Add(session.Scene, accountName, out var account))
        {
            response.ErrorCode = 1;
            return;
        }
        // var account = Entity.Create<Account>(session.Scene);
        account.Session = session;
        // 挂载组件用来标记这个Session下的Account，后面下线流程也会用到
        session.AddComponent<GateAccountFlagComponent>().Account = account;
        // 执行上线流程
        await AccountHelper.Online(session, account);

        // 上线流程完成后,下发属性初始快照到该会话(设计 37 §3.3.1 + plan D3 + O4)。
        // 形态选独立 G2C_PropertyInitSnapshot push message(非登录响应捎带),与 G2C_PropertyDeltaPush 对齐;
        // 客户端段下一刀同一处订阅快照 + 推送两条消息,Player 模块作初视图。
        // 放 Online 之后:确保 GateAccountFlagComponent + account.Session 都已挂全,推送通路稳。
        PlayerPropertyServiceHelper.SendInitSnapshotTo(session, propSnapshot);
    }
}