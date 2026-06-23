using Fantasy.Async;
using Fantasy.Network;

namespace Fantasy;

public static class AccountHelper
{
    /// <summary>
    /// 上线操作。Map 游戏模块已移除,登录不再建立到 Map 的漫游链路,仅绑定会话(Gate-only)。
    /// </summary>
    public static async FTask<uint> Online(Session session, Account account)
    {
        account.Session = session;
        await FTask.CompletedTask;
        return 0;
    }

    /// <summary>
    /// 下线操作。Map 已移除,不再通知 Map 下线,直接从内存移除账号。
    /// </summary>
    public static async FTask Offline(Account account)
    {
        AccountManageHelper.Remove(account.Scene, account.Name);
        await FTask.CompletedTask;
    }
}
