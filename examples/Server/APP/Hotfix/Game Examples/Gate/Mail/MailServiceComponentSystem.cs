using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Entitas.Interface;
using Fantasy.Helper;
using GameConfig.reward;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 邮件服务端组件初始化:绑定原生 MongoDB 集合句柄、建索引、播种运营模板、载入缓存。
/// 领取防重的并发原子性依赖 mail_record 的 _id 复合唯一键(MongoDB 主键天然唯一),见 MailDecisionHelper。
/// 另暴露进程内发奖入口 SendMailTo(设计 32 §3.5):给某账号投一封定向邮件(附件为内联奖励条目列表),供排行榜 / 活动服务端结算复用。
/// 服务端工程无 Luban 集成(无 TbXxx/.bytes 加载链,同设计 30/31 先例),故运营模板以本声明表为权威源、
/// 播种进 MongoDB,与客户端 mail.xlsx 同源(值手抄客户端 xlsx 口径, SV13);附件不再指向礼包随机库,直接内联奖励条目。
/// 设计基线:design-docs/32-mail-server.md §二/§五。
/// </summary>
public sealed class MailServiceComponentAwakeSystem : AwakeSystem<MailServiceComponent>
{
    /// <summary>MongoDB 重复键错误码(与 MailDecisionHelper 同口径)。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    /// <summary>全局过期兜底天数默认值(对应 mail_global.xlsx retain_days=30, SV13)。</summary>
    private const int DefaultGlobalRetainDays = 30;

    /// <summary>发件人 textId 占位(mail.xlsx 无发件人列,服务端给固定占位;客户端查多语言表显示)。</summary>
    private const int DefaultSenderTextId = 110700;

    /// <summary>
    /// 服务端权威运营广播模板表(单一来源)。前 5 条与客户端 mail.xlsx 同源口径一致(SV13):
    ///   - TemplateId    ← mail.xlsx id          (邮件模板 id)
    ///   - TitleTextId   ← mail.xlsx title        (标题多语言 textId)
    ///   - ContentTextId ← mail.xlsx desc         (正文多语言 textId)
    ///   - ExpireDays    ← mail.xlsx expire_days   (有效期天数)
    ///   - RewardId      ← mail.xlsx reward_id     (附件礼包随机库 id;指向 gift_pool 的 Index)
    /// mail.xlsx 的 reward_id=1002 在礼包库未登记(gift_pool 只有 Index=6001),领取按 SV6「成功但奖励列表为空」处理。
    /// 另加 3 条服务端验证样例(覆盖 SV3/SV6/SV2/SV7 各分支,同 redeem 播样例先例,生产可删):
    ///   - 100:reward_id=6001(已登记礼包库),领取可抽出实物(SV3)。
    ///   - 101:reward_id=0(无奖励),领取返 NoReward(SV6)。
    ///   - 102:已过期(发件时间远早 + 有效期 1 天),拉列表不下发 / 领取返 Expired(SV2/SV7)。
    /// SendUnixMs 在播种时按相对当下设定(102 用很早的时间制造过期),见 BuildBroadcastSeeds。
    /// </summary>
    private static IReadOnlyList<MailTemplateDoc> BuildBroadcastSeeds(long nowMs)
    {
        // 102 的过期:发件时间设为 100 天前 + 有效期 1 天 → 早已过期(SV2/SV7)。
        var longAgoMs = nowMs - 100L * 24 * 60 * 60 * 1000;
        return new List<MailTemplateDoc>
        {
            // ── 与客户端 mail.xlsx 同源(id 1-5,内联样例附件:道具 30001 × 1) ──
            new MailTemplateDoc { TemplateId = "1", SenderTextId = DefaultSenderTextId, TitleTextId = 110711, ContentTextId = 110721, ExpireDays = 14, Rewards = SampleItemReward(), SendUnixMs = nowMs },
            new MailTemplateDoc { TemplateId = "2", SenderTextId = DefaultSenderTextId, TitleTextId = 110712, ContentTextId = 110722, ExpireDays = 14, Rewards = SampleItemReward(), SendUnixMs = nowMs },
            new MailTemplateDoc { TemplateId = "3", SenderTextId = DefaultSenderTextId, TitleTextId = 110713, ContentTextId = 110723, ExpireDays = 14, Rewards = SampleItemReward(), SendUnixMs = nowMs },
            new MailTemplateDoc { TemplateId = "4", SenderTextId = DefaultSenderTextId, TitleTextId = 110714, ContentTextId = 110724, ExpireDays = 14, Rewards = SampleItemReward(), SendUnixMs = nowMs },
            new MailTemplateDoc { TemplateId = "5", SenderTextId = DefaultSenderTextId, TitleTextId = 110715, ContentTextId = 110725, ExpireDays = 14, Rewards = SampleItemReward(), SendUnixMs = nowMs },
            // ── 服务端验证样例(生产可删) ──
            // SV3:有实物附件(多条道具全发),领取全部到账。
            new MailTemplateDoc { TemplateId = "100", SenderTextId = DefaultSenderTextId, TitleTextId = 110716, ContentTextId = 110726, ExpireDays = 14, Rewards = SampleMultiItemReward(), SendUnixMs = nowMs },
            // SV6:无奖励邮件(附件空),领取返 NoReward。
            new MailTemplateDoc { TemplateId = "101", SenderTextId = DefaultSenderTextId, TitleTextId = 110717, ContentTextId = 110727, ExpireDays = 14, Rewards = new List<RewardEntryDoc>(), SendUnixMs = nowMs },
            // SV2/SV7:已过期邮件(发件时间 100 天前 + 有效期 1 天)。拉列表不下发,领取返 Expired。
            new MailTemplateDoc { TemplateId = "102", SenderTextId = DefaultSenderTextId, TitleTextId = 110718, ContentTextId = 110728, ExpireDays = 1, Rewards = SampleMultiItemReward(), SendUnixMs = longAgoMs }
        };
    }

    /// <summary>样例附件:道具 30001 × 1(与客户端 mail.xlsx 同源的运营模板占位附件,生产按运营口径替换)。</summary>
    private static List<RewardEntryDoc> SampleItemReward()
    {
        return new List<RewardEntryDoc>
        {
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30001, Amount = 1 }
        };
    }

    /// <summary>多条实物样例附件(全部发放),供验证模板观测多奖励一次全到账(生产可删)。</summary>
    private static List<RewardEntryDoc> SampleMultiItemReward()
    {
        return new List<RewardEntryDoc>
        {
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30001, Amount = 1 },
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30002, Amount = 1 },
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30004, Amount = 1 },
            new RewardEntryDoc { RewardType = (int)ERewardType.Item, TargetId = 30003, Amount = 2 }
        };
    }

    protected override void Awake(MailServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    private static async FTask Init(MailServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):领取记录无法持久,领取裁决会返「服务不可用」。
            // 这对应交接区 BLOCKED-环境:逻辑就绪、运行依赖外部 MongoDB。属预期环境条件,用 Warning 不用 Error。
            Log.Warning("MailServiceComponent: MongoDB 实例不可用,邮件拉列表/领取将返回 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            return;
        }

        var templates = mongoDatabase.GetCollection<MailTemplateDoc>("mail_template");
        self.Templates = templates;
        self.Directed = mongoDatabase.GetCollection<MailDirectedDoc>("mail_directed");
        self.Records = mongoDatabase.GetCollection<MailClaimRecordDoc>("mail_record");
        self.GlobalRetainDays = DefaultGlobalRetainDays;

        // 定向邮件按账号查询应收,建 Account 索引贴合该访问模式(拉列表筛该账号定向邮件)。
        await CreateDirectedAccountIndex(self.Directed);

        // 首次启动播种运营模板(已存在则跳过,不覆盖运营改动)。
        await SeedBroadcastTemplates(templates);

        // 载入模板到内存缓存(只读裁决用,领取防重始终走 MongoDB)。
        await ReloadTemplateCache(self);

        Log.Info($"MailServiceComponent 初始化完成,运营模板缓存条目数={self.TemplateCache.Count}");
    }

    /// <summary>建定向邮件 Account 索引(拉列表按账号筛该账号的定向邮件)。</summary>
    private static async FTask CreateDirectedAccountIndex(IMongoCollection<MailDirectedDoc> directed)
    {
        var keys = Builders<MailDirectedDoc>.IndexKeys.Ascending(x => x.Account);
        await directed.Indexes.CreateOneAsync(new CreateIndexModel<MailDirectedDoc>(keys));
    }

    /// <summary>重新载入运营模板缓存。供启动与(未来)运营热改后刷新。</summary>
    public static async FTask ReloadTemplateCache(MailServiceComponent self)
    {
        if (self.Templates == null)
        {
            return;
        }
        self.TemplateCache.Clear();
        var all = await self.Templates.Find(FilterDefinition<MailTemplateDoc>.Empty).ToListAsync();
        foreach (var doc in all)
        {
            self.TemplateCache[doc.TemplateId] = doc;
        }
    }

    /// <summary>
    /// 播种运营广播模板,仅当对应模板 id 不存在时插入(不覆盖运营已配置/已改的模板)。
    /// 幂等性靠 _id(TemplateId)主键唯一保证:重复插入抛 DuplicateKey 即「已播种 / 已存在」,捕获后跳过续插。
    /// 这使播种在「多 Gate Scene 并发首启」与「服务端重启」两种场景都安全
    /// (不做先读后写的存在性预检——那是 check-then-act 竞态,并发两个 Gate 可同时通过预检再各自插入)。
    /// </summary>
    private static async FTask SeedBroadcastTemplates(IMongoCollection<MailTemplateDoc> templates)
    {
        foreach (var seed in BuildBroadcastSeeds(TimeHelper.Now))
        {
            try
            {
                await templates.InsertOneAsync(seed);
            }
            catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // 已存在 → 视为已播种,跳过。
            }
            catch (MongoCommandException e) when (e.Code == DuplicateKeyErrorCode)
            {
                // 已存在 → 视为已播种,跳过。
            }
        }
    }

}

public sealed class MailServiceComponentDestroySystem : DestroySystem<MailServiceComponent>
{
    protected override void Destroy(MailServiceComponent self)
    {
        self.TemplateCache.Clear();
        self.Templates = null;
        self.Directed = null;
        self.Records = null;
    }
}
