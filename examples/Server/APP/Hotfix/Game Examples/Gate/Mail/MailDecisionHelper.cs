using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 邮件裁决核心逻辑(服务端唯一权威)。
/// 拉列表:该账号应收 = 活跃广播模板 + 该账号定向邮件,滤过期(服务端时钟),附每封领取态。
/// 领取:定位邮件 → 过期(服务端时钟) → 无奖励短路 → 按账号防重(原子唯一写) → 返回邮件内嵌附件(全部发放,不抽奖)。
/// 并发原子性:
///   - 按账号防重:mail_record 以 _id="{account}|{mailId}" 唯一,重复插入抛 DuplicateKey(SV4/SV5 不双领)。
///     「检查未领过 + 记录已领」合并为单次原子写;认领在记录成功之后(记录成功才认领、才发奖),
///     不做「先发奖响应、再记录」(那会并发双领,设计 §五)。
/// 失败/边界分支不写记录、不抛异常,以结果码回包(SV8/SV11)。
/// 设计基线:design-docs/32-mail-server.md §二/§三/§五。
/// </summary>
public static class MailDecisionHelper
{
    /// <summary>MongoDB 重复键错误码。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>对外邮件标识前缀:广播模板。</summary>
    private const string BroadcastPrefix = "t";

    /// <summary>对外邮件标识前缀:定向邮件。</summary>
    private const string DirectedPrefix = "d";

    // ── 拉邮件列表 ──────────────────────────────────────────────

    /// <summary>
    /// 拉一个账号应收的邮件列表。account 为服务端从会话取得的设备账号(非客户端自报)。
    /// 应收 = 活跃广播模板(全服) + 该账号定向邮件,均滤掉已过期(服务端时钟);每封附该账号领取态。
    /// 返回结果码(Success / ServiceUnavailable) + 邮件列表项。
    /// </summary>
    public static async FTask<(MailClaimResultCode resultCode, List<MailListItem> mails)> List(
        MailServiceComponent self, string account)
    {
        var emptyMails = new List<MailListItem>();

        // 服务未就绪(MongoDB 不可达):返「服务不可用」,客户端显示空列表 / 提示,不阻断玩法(设计 §四)。
        if (self.Templates == null || self.Directed == null || self.Records == null)
        {
            return (MailClaimResultCode.ServiceUnavailable, emptyMails);
        }

        var nowMs = TimeHelper.Now;

        // 该账号已领记录:一次查出,供列表标领取态(O(1) 命中,不逐封查库)。
        var claimedSet = await QueryClaimedSet(self.Records, account);

        var mails = new List<MailListItem>();

        // 广播模板(全服应收):缓存遍历,滤过期。
        foreach (var template in self.TemplateCache.Values)
        {
            if (IsExpired(template.SendUnixMs, template.ExpireDays, self.GlobalRetainDays, nowMs))
            {
                continue; // 已过期不下发(SV2)。
            }
            var mailId = BroadcastPrefix + template.TemplateId;
            mails.Add(BuildListItem(
                mailId, template.Sender, template.Title, template.Content,
                template.SendUnixMs, template.Rewards, claimedSet.Contains(mailId)));
        }

        // 定向邮件(仅该账号应收):按 Account 查,滤过期。
        var directedFilter = Builders<MailDirectedDoc>.Filter.Eq(x => x.Account, account);
        var directedDocs = await self.Directed.Find(directedFilter).ToListAsync();
        foreach (var doc in directedDocs)
        {
            if (IsExpired(doc.SendUnixMs, doc.ExpireDays, self.GlobalRetainDays, nowMs))
            {
                continue;
            }
            var mailId = DirectedPrefix + doc.DirectedId;
            mails.Add(BuildListItem(
                mailId, doc.Sender, doc.Title, doc.Content,
                doc.SendUnixMs, doc.Rewards, claimedSet.Contains(mailId)));
        }

        return (MailClaimResultCode.Success, mails);
    }

    /// <summary>查该账号全部已领邮件标识集合(供列表标领取态)。</summary>
    private static async FTask<HashSet<string>> QueryClaimedSet(IMongoCollection<MailClaimRecordDoc> records, string account)
    {
        var filter = Builders<MailClaimRecordDoc>.Filter.Eq(x => x.Account, account);
        var docs = await records.Find(filter).ToListAsync();
        var set = new HashSet<string>();
        foreach (var d in docs)
        {
            set.Add(d.MailId);
        }
        return set;
    }

    /// <summary>
    /// 列表项走对象池 Create():随响应一起发送,响应 Dispose 时归还池(零 GC 范式)。
    /// 内嵌 RewardEntryDoc → MailListItem.Rewards(类型+目标id+数量),供客户端领取前预览「给什么」;
    /// HasReward 由明细非空派生。奖励项同走对象池 Create(与领取响应 Rewards 填充同范式)。
    /// 成本:每封几条明细、对象池无 GC、拉列表低频且邮件条数小,可忽略(不优化)。
    /// </summary>
    private static MailListItem BuildListItem(
        string mailId, MailSenderType sender, string title, string content,
        long sendUnixMs, List<RewardEntryDoc> rewards, bool claimed)
    {
        var item = MailListItem.Create();
        item.MailId = mailId;
        item.Sender = sender;
        item.Title = title;
        item.Content = content;
        item.SendUnixMs = sendUnixMs;
        item.HasReward = rewards != null && rewards.Count > 0;
        item.Claimed = claimed;
        if (rewards != null)
        {
            foreach (var entry in rewards)
            {
                var r = MailRewardItem.Create();
                r.RewardType = entry.RewardType;
                r.TargetId = entry.TargetId;
                r.Amount = entry.Amount;
                item.Rewards.Add(r);
            }
        }
        return item;
    }

    // ── 领取奖励 ──────────────────────────────────────────────

    /// <summary>
    /// 裁决一次领取。account 为服务端从会话取得的设备账号(非客户端自报)。mailId 为对外邮件标识(含 t/d 前缀)。
    /// 返回结果码 + 成功时的内联奖励条目列表(失败时列表为空);奖励为邮件内嵌附件,全部发放(不抽奖)。
    /// 顺序:定位(存在 + 属于该账号) → 过期(服务端时钟) → 无奖励短路 → 原子防重写 → 取内嵌附件全发。
    /// </summary>
    public static async FTask<(MailClaimResultCode resultCode, List<RewardEntryDoc> rewards)> Claim(
        MailServiceComponent self, string account, string mailId)
    {
        var emptyRewards = new List<RewardEntryDoc>();

        // 服务未就绪:返「服务不可用」,不发奖、邮件保持可领(设计 §四,不可本地放行)。
        if (self.Templates == null || self.Directed == null || self.Records == null)
        {
            return (MailClaimResultCode.ServiceUnavailable, emptyRewards);
        }
        var records = self.Records;

        // 1. 定位邮件(存在 + 属于该账号):取该邮件的有效期 + 内嵌附件。定位不到 → 邮件不存在(SV8)。
        var located = await Locate(self, account, mailId);
        if (located == null)
        {
            return (MailClaimResultCode.MailNotFound, emptyRewards);
        }
        var (sendUnixMs, expireDays, rewards) = located.Value;

        // 2. 过期判定以服务端时钟为准(SV7),下发已滤过期但下发后到期再领须再判一次。
        if (IsExpired(sendUnixMs, expireDays, self.GlobalRetainDays, TimeHelper.Now))
        {
            return (MailClaimResultCode.Expired, emptyRewards);
        }

        // 3. 无奖励邮件(附件空):纯通知,不发奖、不记录(无奖励可双领, SV6)。
        if (rewards.Count == 0)
        {
            return (MailClaimResultCode.NoReward, emptyRewards);
        }

        // 4. 按账号防重:原子唯一写。重复即「已领过」(SV4/SV5/SV11)。
        //    记录在发奖之前:记录成功才认领、才发奖(防并发双领, 设计 §五)。
        if (!await TryWriteRecord(records, account, mailId))
        {
            return (MailClaimResultCode.AlreadyClaimed, emptyRewards);
        }

        // 5. 认领成功:返回邮件内嵌附件(全部发放,客户端不申报;权威到账由 Handler 调发放器完成, §五)。
        return (MailClaimResultCode.Success, rewards);
    }

    /// <summary>
    /// 定位邮件:按对外标识(t=广播 / d=定向)取该邮件的(发件时间, 有效期天数, 内嵌附件列表)。
    /// 广播取缓存模板;定向查库且校验属于该账号(不属于该账号 → 视作不存在,防领他人邮件, SV8/SV9)。
    /// 定位不到返回 null。
    /// </summary>
    private static async FTask<(long sendUnixMs, int expireDays, List<RewardEntryDoc> rewards)?> Locate(
        MailServiceComponent self, string account, string mailId)
    {
        if (string.IsNullOrEmpty(mailId) || mailId.Length < 2)
        {
            return null;
        }

        var prefix = mailId.Substring(0, 1);
        var rawId = mailId.Substring(1);

        if (prefix == BroadcastPrefix)
        {
            if (self.TemplateCache.TryGetValue(rawId, out var template))
            {
                return (template.SendUnixMs, template.ExpireDays, template.Rewards);
            }
            return null;
        }

        if (prefix == DirectedPrefix && self.Directed != null)
        {
            var filter = Builders<MailDirectedDoc>.Filter.And(
                Builders<MailDirectedDoc>.Filter.Eq(x => x.DirectedId, rawId),
                Builders<MailDirectedDoc>.Filter.Eq(x => x.Account, account));
            var doc = await self.Directed.Find(filter).FirstOrDefaultAsync();
            if (doc != null)
            {
                return (doc.SendUnixMs, doc.ExpireDays, doc.Rewards);
            }
            return null;
        }

        return null;
    }

    /// <summary>
    /// 原子写入按账号领取记录:_id="{account}|{mailId}" 唯一。
    /// 首次写入返回 true(可发奖);重复(DuplicateKey)返回 false(= 已领过)。
    /// 这一步合并了「检查未领过 + 记录已领」,是并发防重领的原子点(SV5)。
    /// </summary>
    private static async FTask<bool> TryWriteRecord(IMongoCollection<MailClaimRecordDoc> records, string account, string mailId)
    {
        var doc = new MailClaimRecordDoc
        {
            UniqueKey = $"{account}|{mailId}",
            Account = account,
            MailId = mailId,
            ClaimedUnixMs = TimeHelper.Now
        };

        try
        {
            await records.InsertOneAsync(doc);
            return true;
        }
        catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
        catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
        {
            return false;
        }
    }

    // ── 进程内发奖入口(设计 §3.5) ────────────────────────────

    /// <summary>
    /// 服务端进程内发奖入口:给某账号投一封定向邮件(初始未领),供排行榜 / 活动服务端结算复用(SV10)。
    /// 走与运营邮件同一套领取 / 防重机制——只往 mail_directed 插一条,附件为内联奖励条目列表(领取时全部发放),
    /// 该账号下次拉列表即可见、可领。rewards 为 null / 空 → 投一封无奖励通知邮件。
    /// 投递成功且目标玩家在线时,推 G2C_MailNotify 主动通知(纯信号,客户端据此拉列表刷红点);离线不推(下次登录拉列表照常)。
    /// 返回投递的对外邮件标识("d{guid}");服务不可用(MongoDB 未就绪)返回 null(调用方据此重试)。
    /// 注:发奖入口只负责「投一封」;结算幂等(同一次结算只投一次)是调用方的责任(设计 §五),不在本入口。
    /// </summary>
    public static async FTask<string?> SendMailTo(
        MailServiceComponent self, string account,
        MailSenderType sender, string title, string content, int expireDays, IReadOnlyList<RewardEntryDoc>? rewards)
    {
        if (self.Directed == null)
        {
            return null;
        }

        var directedId = Guid.NewGuid().ToString("N");
        var doc = new MailDirectedDoc
        {
            DirectedId = directedId,
            Account = account,
            Sender = sender,
            Title = title,
            Content = content,
            ExpireDays = expireDays,
            Rewards = rewards != null ? new List<RewardEntryDoc>(rewards) : new List<RewardEntryDoc>(),
            SendUnixMs = TimeHelper.Now
        };
        try
        {
            await self.Directed.InsertOneAsync(doc);
        }
        catch (MongoException e)
        {
            // 兑现契约「服务不可用返 null」:MongoDB 抖动 / 未就绪时插入抛异常,返 null 让调用方据此重试,不外泄异常。
            Log.Warning($"MailDecisionHelper.SendMailTo 投递失败 account={account} rewardCount={(rewards?.Count ?? 0)},err={e.Message}");
            return null;
        }

        // 新邮件主动通知:目标玩家在线则推一个纯信号(客户端收到拉列表刷新收件箱 / 红点,红点仍基于服务端权威列表重算,
        // 信号不携带邮件数据);离线丢弃——下次登录拉列表照常(不重试,同 delta-push O6)。定向推送低频(每次发信一次、
        // 只推目标一人,非全量扇出),复用 delta-push 的在线会话定位范式(AccountManageHelper.TryGetAccount → Session.Send)。
        // 加在 SendMailTo 内部:排行榜结算 / GM / 活动结算等发信线经此入口自动覆盖(改一处治多线)。
        if (AccountManageHelper.TryGetAccount(self.Scene, account, out var onlineAccount))
        {
            Session session = onlineAccount.Session; // EntityReference<Session> 隐式解包(同 SendDeltaPushTo 范式)
            if (session != null && !session.IsDisposed)
            {
                session.Send(new G2C_MailNotify());
            }
        }

        return DirectedPrefix + directedId;
    }

    // ── 工具 ──────────────────────────────────────────────────

    /// <summary>
    /// 过期判定(服务端时钟):now &gt; 发件时间 + 有效期天数。
    /// 有效期 &lt;=0 用全局 retain_days 兜底(设计决策, mail_global retain_days=30)。
    /// </summary>
    private static bool IsExpired(long sendUnixMs, int expireDays, int globalRetainDays, long nowMs)
    {
        var effectiveDays = expireDays > 0 ? expireDays : globalRetainDays;
        var expireAtMs = sendUnixMs + (long)effectiveDays * 24 * 60 * 60 * 1000;
        return nowMs > expireAtMs;
    }

    // ── 清档 ──────────────────────────────────────────────────

    /// <summary>
    /// 清档·删除某账号的全部 per-player 邮件数据(按玩家身份 Account 删,非 _id)。
    /// 两张集合都按 Account 持有该玩家多行 → 1:N → DeleteMany:
    ///   - mail_directed:投给该账号的全部定向邮件;
    ///   - mail_record:该账号的全部领取记录(清后此前已领的邮件回到「可再领」态)。
    /// 不动 mail_template / gift_pool(全服运营广播模板 + 全局奖池,非 per-player)。
    /// 两步任一失败 → 返 false;两步都成功(含 0 匹配)→ 返 true。幂等:重复清 0 匹配仍成功。
    /// </summary>
    public static async FTask<bool> ClearByAccount(MailServiceComponent self, string account)
    {
        if (self.Directed == null || self.Records == null)
        {
            return false;
        }
        if (string.IsNullOrEmpty(account))
        {
            return true;
        }

        try
        {
            var directedFilter = Builders<MailDirectedDoc>.Filter.Eq(x => x.Account, account);
            var directedResult = await self.Directed.DeleteManyAsync(directedFilter);

            var recordFilter = Builders<MailClaimRecordDoc>.Filter.Eq(x => x.Account, account);
            var recordResult = await self.Records.DeleteManyAsync(recordFilter);

            Log.Debug($"Mail 清档删除 account={account} directedDeleted={directedResult.DeletedCount} recordDeleted={recordResult.DeletedCount}");
            return true;
        }
        catch (MongoException e)
        {
            Log.Warning($"MailDecisionHelper.ClearByAccount 失败 account={account},err={e.Message}");
            return false;
        }
    }
}
