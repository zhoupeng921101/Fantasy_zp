using Fantasy.Async;
using Fantasy.Helper;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 改名服务端权威裁决核心(云存档 blob 退役·第 2 批·子批 2a)。
///
/// 昵称与改名次数服务端权威(PlayerDoc.Nickname / RenameCount);改名费服务端派生(RenameConfigServer.PriceFor)。
/// 处理顺序(与客户端 PlayerRenameService.TryRename 同判定次序,身份/费用/次数一律服务端说了算):
///   ① 基本 sanity:非空 / 不全空白 / 长度 ≤ RenameConfigServer.MaxNicknameLength;
///   ①.5 屏蔽字终审:ProfanityFilterServer.IsClean 服务端裁定(前端预检不作数);机制已就位、词表待填(空表恒过);
///   ② 读 doc 拿当前 RenameCount + Diamond(未首登 → ServiceUnavailable);
///   ③ 算费:RenameCount==0 免费,否则 PriceFor(RenameCount);
///   ④ 需扣费 → PlayerPropertyServiceHelper.ChangeProperty(Diamond, -cost, serverAuthoritative:true,
///      reason="player_rename")。钻不足(NotEnough)→ 回 NotEnoughDiamond、不改名;服务不可用同理短路;
///   ⑤ 扣成功(或免费)→ 单条原子 FindOneAndUpdate:$set(Nickname) + $inc(RenameCount, 1)。
///
/// 反作弊红线:请求只带新昵称,**不**接受客户端上报费用 / 次数;费用由服务端按自己的 RenameCount 算定,
/// 走 serverAuthoritative=true 的 ChangeProperty(上界 cap + 原子写 + ledger + 推送,跳过客户端 RPC 路径单笔上限 / 频率闸)。
///
/// 并发:当前 demo 每 UUID 只一个在线会话(AccountManageComponentSystem.Add 同 UUID 二次登录返 false),
/// 同账号并发改名实际不可能,故 ④→⑤ 之间读到的 RenameCount 与 ⑤ 的 $inc 之间无 TOCTOU 竞争,
/// 不额外 CAS RenameCount(真需多会话时,⑤ 改为 filter 含 RenameCount==读到值的 CAS)。
///
/// 失败一律以 ResultCode 回包,不抛异常断连;时钟统一 TimeHelper.Now。
/// 设计基线:.claude/rules/data-authority.md(身份/修饰服务端权威、费用服务端派生、限界信任)。
/// </summary>
public static class RenameHelper
{
    /// <summary>
    /// 执行改名裁定。返回 (resultCode, nickname, renameCount, diamond):
    ///   - Success:nickname = 新名、renameCount = +1 后、diamond = 扣后余额(免费则未变);
    ///   - InvalidName / NotEnoughDiamond / ServiceUnavailable:nickname / renameCount = 服务端当前权威值(便于客户端回退),
    ///     diamond = 当前余额(读失败为 0)。
    /// </summary>
    public static async FTask<(RenameResultCode resultCode, string nickname, int renameCount, long diamond)> TryRename(
        Scene scene, string accountId, string newNickname)
    {
        var service = scene.GetComponent<PlayerPropertyServiceComponent>();
        if (service?.Players is not { } players)
        {
            return (RenameResultCode.ServiceUnavailable, string.Empty, 0, 0L);
        }

        // ① 基本 sanity:非空 / 不全空白 / 长度上限。客户端已做完整校验,服务端只挡最基本非法。
        var name = newNickname ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > RenameConfigServer.MaxNicknameLength)
        {
            // 读当前权威值回带(供客户端回退显示);读失败也返 InvalidName(名字非法优先于服务读失败)。
            var (curName, curCount, curDiamond) = await ReadCurrent(players, accountId);
            return (RenameResultCode.InvalidName, curName, curCount, curDiamond);
        }

        // ①.5 屏蔽字服务端终审(合规底线:前端预检不作数,服务端裁定)。命中屏蔽词返 InvalidName + 当前权威值(供客户端回退)。
        if (!ProfanityFilterServer.IsClean(name))
        {
            var (curName, curCount, curDiamond) = await ReadCurrent(players, accountId);
            return (RenameResultCode.InvalidName, curName, curCount, curDiamond);
        }

        // ② 读 doc 拿当前 RenameCount + Diamond。未首登(理论上改名前必已登录,防御)→ 服务不可用。
        PlayerDoc? doc;
        try
        {
            doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
        }
        catch (MongoException e)
        {
            Log.Warning($"RenameHelper.TryRename 读文档失败 account={accountId},err={e.Message}");
            return (RenameResultCode.ServiceUnavailable, string.Empty, 0, 0L);
        }
        if (doc == null)
        {
            Log.Warning($"RenameHelper.TryRename:账号 {accountId} 在 players 集合不存在(未首登?),返 ServiceUnavailable。");
            return (RenameResultCode.ServiceUnavailable, string.Empty, 0, 0L);
        }

        var renameCount = doc.RenameCount;
        var diamond = doc.Diamond;

        // ③ 算费:首次免费,否则固定价(与客户端 RenamePriceConfig 同口径)。
        var cost = RenameConfigServer.PriceFor(renameCount);

        // ④ 需扣费 → 服务端权威扣钻。钻不足 / 服务不可用短路,不改名。
        if (cost > 0)
        {
            var (chargeCode, newDiamond) = await PlayerPropertyServiceHelper.ChangeProperty(
                scene, accountId, PropertyType.Diamond, -cost, "player_rename", serverAuthoritative: true);
            if (chargeCode == PropertyChangeResultCode.NotEnough)
            {
                // 钻不足:newDiamond = 当前实际余额(ChangeProperty 匹配失败时回带)。
                return (RenameResultCode.NotEnoughDiamond, doc.Nickname, renameCount, newDiamond);
            }
            if (chargeCode != PropertyChangeResultCode.Success)
            {
                // ServiceUnavailable / 其它异常:昵称未改。
                return (RenameResultCode.ServiceUnavailable, doc.Nickname, renameCount, diamond);
            }
            // 扣费成功:ChangeProperty 已写 ledger(内部),但**不**自动起推送(推送时机由调用方控)。
            // 与 C2G_PropertyChangeRequestHandler 同范式,成功后显式起 Diamond 的 delta 推送,
            // 让所有 Diamond 消费者(不止读改名响应的那一刀)都收到权威新余额,避免通用属性视图与改名响应两条 Diamond 值分叉。
            PlayerPropertyServiceHelper.SendDeltaPushTo(scene, accountId, PropertyType.Diamond, newDiamond, "player_rename");
            diamond = newDiamond;
        }

        // ⑤ 扣成功(或免费)→ 原子写昵称 + 次数 +1。
        var nowMs = TimeHelper.Now;
        var filter = Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId);
        var update = Builders<PlayerDoc>.Update
            .Set(x => x.Nickname, name)
            .Inc(x => x.RenameCount, 1)
            .Set(x => x.LastChangeUnixMs, nowMs);
        var options = new FindOneAndUpdateOptions<PlayerDoc>
        {
            IsUpsert = false,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var updated = await players.FindOneAndUpdateAsync(filter, update, options);
            if (updated == null)
            {
                // 理论上 doc 刚读到、账号必存在;并发被删档等极端 → 服务不可用(此时钻可能已扣,记 Warning)。
                Log.Warning($"RenameHelper.TryRename:写昵称时文档不存在 account={accountId}(钻可能已扣 cost={cost}),返 ServiceUnavailable。");
                return (RenameResultCode.ServiceUnavailable, doc.Nickname, renameCount, diamond);
            }
            Log.Info($"改名成功 account={accountId} nickname='{updated.Nickname}' renameCount={updated.RenameCount} cost={cost} diamond={diamond}");
            return (RenameResultCode.Success, updated.Nickname, updated.RenameCount, diamond);
        }
        catch (MongoException e)
        {
            Log.Warning($"RenameHelper.TryRename 写昵称失败 account={accountId}(钻可能已扣 cost={cost}),err={e.Message}");
            return (RenameResultCode.ServiceUnavailable, doc.Nickname, renameCount, diamond);
        }
    }

    /// <summary>读当前权威昵称 / 次数 / 钻石(失败或无档返空串 / 0)。仅供失败分支回带客户端回退显示。</summary>
    private static async FTask<(string nickname, int renameCount, long diamond)> ReadCurrent(
        IMongoCollection<PlayerDoc> players, string accountId)
    {
        try
        {
            var doc = await players.Find(Builders<PlayerDoc>.Filter.Eq(x => x.AccountId, accountId)).FirstOrDefaultAsync();
            if (doc == null) return (string.Empty, 0, 0L);
            return (doc.Nickname, doc.RenameCount, doc.Diamond);
        }
        catch (MongoException)
        {
            return (string.Empty, 0, 0L);
        }
    }
}
