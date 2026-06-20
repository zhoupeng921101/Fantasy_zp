using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 账号账本登录裁决:首连自动注册 / 重连刷新末次登录,二合一(plan §3.2 「认证 = 注册 + 登录二合一」)。
/// 单条原子 upsert:`UpdateOneAsync(filter: _id == accountId, update: $setOnInsert(FirstLoginUnixMs/Status=0) + $set(LastLoginUnixMs), upsert: true)`。
/// MongoDB 单条命令原子,SV7 并发同 UUID 双登 → 一次走 insert / 一次走 update,主键不冲突、首次注册时间稳定。
/// 状态字段 Status 仅 $setOnInsert 写默认 0,update 路径不触碰 → Tier 1+ 运营改写封禁不被本子单重置(SV5)。
/// 失败 → 返非 0 错误码,Handler 短路后续步骤(不挂会话身份,沿用 30 「服务不可用不本地放行」)。
/// 设计基线:design-docs/35-account-server.md §3.2 + §五。
/// </summary>
public static class AccountServiceHelper
{
    /// <summary>登录失败错误码(沿用既有 LoginGameRequestHandler 的简化错误码体系,后续要细分由 O4 决定)。</summary>
    public const uint LoginErrorCode = 1;

    /// <summary>
    /// 首连自动注册 / 重连刷新末次登录。返 0 = 成功,非 0 = 失败(MongoDB 不可达 / 异常)。
    /// 单条原子 upsert,见类文档。
    /// </summary>
    public static async FTask<uint> RegisterOrLogin(Scene scene, string accountId)
    {
        var component = scene.GetComponent<AccountServiceComponent>();
        if (component == null)
        {
            Log.Error("当前 Scene 下没有找到 AccountServiceComponent 组件(应挂在 Gate Scene 上)。");
            return LoginErrorCode;
        }

        var accounts = component.Accounts;
        if (accounts == null)
        {
            // MongoDB 不可达 — AwakeSystem 已 Warning,此处不重复 Warning。
            // 登录失败短路:不挂会话身份(plan §3.2 失败硬约束 + 30 「服务不可用不本地放行」)。
            return LoginErrorCode;
        }

        var nowMs = TimeHelper.Now;
        var filter = Builders<AccountDoc>.Filter.Eq(x => x.AccountId, accountId);
        // $setOnInsert:首次注册时间 + 状态默认 0,仅 insert 时写入(后续 update 路径完全不触碰,SV4/SV5)。
        // $set:末次登录时间,每次都刷新(SV3/SV4)。
        // upsert: true:不存在则 insert / 存在则 update,单条原子(SV7)。
        var update = Builders<AccountDoc>.Update
            .SetOnInsert(x => x.AccountId, accountId)
            .SetOnInsert(x => x.FirstLoginUnixMs, nowMs)
            .SetOnInsert(x => x.Status, 0)
            .Set(x => x.LastLoginUnixMs, nowMs);
        var options = new UpdateOptions { IsUpsert = true };

        try
        {
            await accounts.UpdateOneAsync(filter, update, options);
            return 0;
        }
        catch (MongoException e)
        {
            // upsert 失败(写入异常 / 网络抖动 / 集群挂):登录失败、短路后续、不挂会话身份。
            // 不抛异常断连(沿用 Fantasy.Net 错误码非异常基线)。
            Log.Warning($"AccountServiceHelper.RegisterOrLogin 失败,accountId={accountId},err={e.Message}");
            return LoginErrorCode;
        }
    }
}
