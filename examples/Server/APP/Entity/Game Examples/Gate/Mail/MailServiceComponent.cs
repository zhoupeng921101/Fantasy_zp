using System.Collections.Generic;
using Fantasy.Entitas;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 邮件服务端权威组件,挂在 Gate Scene 上(玩家会话所在、且其 World 配了 MongoDB)。
/// 持有四张原生 MongoDB 集合句柄(运营模板 / 定向邮件 / 领取记录 / 礼包随机库)与模板/礼包库内存缓存,
/// 供拉列表 / 领取 Handler 与进程内发奖入口使用。
/// 领奖防重的并发原子性由 mail_record 的 _id 复合唯一键(MongoDB 主键天然唯一)保证,见 MailDecisionHelper。
/// 设计基线:design-docs/32-mail-server.md。
/// </summary>
public sealed class MailServiceComponent : Entity
{
    /// <summary>运营广播邮件模板集合(mail_template),_id = 模板 id。Init 前 / MongoDB 不可达时为 null。</summary>
    public IMongoCollection<MailTemplateDoc>? Templates;

    /// <summary>系统定向邮件集合(mail_directed),_id = 定向邮件唯一 id。Init 前为 null。</summary>
    public IMongoCollection<MailDirectedDoc>? Directed;

    /// <summary>按账号领取记录集合(mail_record),_id = "{account}|{mailId}" 唯一键。Init 前为 null。</summary>
    public IMongoCollection<MailClaimRecordDoc>? Records;

    /// <summary>礼包随机库集合(gift_pool),服务端抽奖用。Init 前为 null。</summary>
    public IMongoCollection<GiftPoolEntryDoc>? GiftPool;

    /// <summary>
    /// 运营广播模板缓存(模板 id → 模板)。启动时从 mail_template 载入,运行时只读裁决用。
    /// 缓存仅服务「模板存在性 / 标题 / 正文 / 有效期 / 附件库 id」这类静态配置查询;
    /// 领取防重的权威永远走 mail_record 的 MongoDB 原子写,不读缓存(避免并发判断走偏)。
    /// </summary>
    public readonly Dictionary<string, MailTemplateDoc> TemplateCache = new Dictionary<string, MailTemplateDoc>();

    /// <summary>
    /// 礼包随机库缓存(库 Index → 该 Index 下全部条目)。启动时从 gift_pool 载入,领取时按权重抽一条。
    /// 抽奖只读配置缓存即可(奖励额度的权威 = 服务端这份同源配置,非客户端申报);记录已领仍走 MongoDB 原子写。
    /// </summary>
    public readonly Dictionary<int, List<GiftPoolEntryDoc>> GiftPoolCache = new Dictionary<int, List<GiftPoolEntryDoc>>();

    /// <summary>全局过期兜底天数(有效期 &lt;=0 的邮件用此值)。启动从 mail_global 的 retain_days 载入,默认 30。</summary>
    public int GlobalRetainDays = 30;
}
