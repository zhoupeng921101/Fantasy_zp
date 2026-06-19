using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 邮件服务端存储的原生 MongoDB 文档定义(非框架 Entity)。
// 领奖防重需要 MongoDB 原子条件操作(唯一键插入)防并发双领,
// 框架 IDatabase 高层 API 只能先读后写(设计 32 §五 明令禁止),故直接用原生 IMongoDatabase + BSON 文档。
// 设计基线:design-docs/32-mail-server.md §二/§五。

/// <summary>
/// 运营广播邮件模板文档:运营在服务端配置的「模板 / 标题 / 正文 textId / 附件库 id / 有效期」。
/// 集合 mail_template;_id = 模板 id 字符串(与客户端 mail.xlsx 的 id 同源, SV13)。
/// 全服广播:每个账号都应收所有活跃(未过期)模板,减去该账号已领过的(已领仍下发但标已领态)。
/// 对外协议中该邮件的标识 MailId = "t{TemplateId}"(广播与定向用同一标识空间, 见 MailDecisionHelper)。
/// </summary>
public sealed class MailTemplateDoc
{
    /// <summary>模板 id 字符串,作为 _id 主键(取客户端 mail.xlsx 的 id, SV13)。</summary>
    [BsonId]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>发件人(多语言 textId 占位)。mail.xlsx 无此列,服务端给固定占位。</summary>
    public int SenderTextId { get; set; }

    /// <summary>标题(多语言 textId)。对应 mail.xlsx title。</summary>
    public int TitleTextId { get; set; }

    /// <summary>正文(多语言 textId)。对应 mail.xlsx desc。</summary>
    public int ContentTextId { get; set; }

    /// <summary>有效期(天);&lt;=0 用全局 retain_days 兜底。对应 mail.xlsx expire_days。</summary>
    public int ExpireDays { get; set; }

    /// <summary>附件礼包随机库 id(0 = 无奖励;指向 gift_pool 的 Index)。对应 mail.xlsx reward_id。</summary>
    public int RewardId { get; set; }

    /// <summary>发件/收件时间基线(服务端 Unix 毫秒, UTC),过期 = 此时间 + 有效期天数。</summary>
    public long SendUnixMs { get; set; }
}

/// <summary>
/// 系统定向邮件文档:服务端进程内发奖入口(设计 32 §3.5)投给指定账号的邮件。
/// 集合 mail_directed;_id = 生成的定向邮件唯一 id(GUID)。
/// 仅 Account 字段所指账号应收;对外标识 MailId = "d{DirectedId}"。
/// 与广播模板走同一套领取 / 防重 / 抽奖机制(领取记录都按 (账号,MailId) 原子写)。
/// </summary>
public sealed class MailDirectedDoc
{
    /// <summary>定向邮件唯一 id,作为 _id 主键(发奖入口生成的 GUID)。</summary>
    [BsonId]
    public string DirectedId { get; set; } = string.Empty;

    /// <summary>目标账号(只有此账号应收此邮件)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>发件人(多语言 textId 占位)。</summary>
    public int SenderTextId { get; set; }

    /// <summary>标题(多语言 textId)。</summary>
    public int TitleTextId { get; set; }

    /// <summary>正文(多语言 textId)。</summary>
    public int ContentTextId { get; set; }

    /// <summary>有效期(天);&lt;=0 用全局 retain_days 兜底。</summary>
    public int ExpireDays { get; set; }

    /// <summary>附件礼包随机库 id(0 = 无奖励;指向 gift_pool 的 Index)。</summary>
    public int RewardId { get; set; }

    /// <summary>发件/收件时间(服务端 Unix 毫秒, UTC),过期 = 此时间 + 有效期天数。</summary>
    public long SendUnixMs { get; set; }
}

/// <summary>
/// 按账号领取记录:每条 = 某账号已领过某封邮件。
/// 集合 mail_record;_id = "{account}|{mailId}" 复合唯一键(mailId 含 t/d 前缀,广播与定向不撞键)。
/// 第二次插入同 _id 抛 DuplicateKey → 裁定「已领过」(原子条件写, SV4/SV5)。
/// </summary>
public sealed class MailClaimRecordDoc
{
    /// <summary>"{account}|{mailId}" 复合唯一键,作为 _id 主键。</summary>
    [BsonId]
    public string UniqueKey { get; set; } = string.Empty;

    /// <summary>账号(从会话取的设备账号,非客户端自报)。</summary>
    public string Account { get; set; } = string.Empty;

    /// <summary>对外邮件标识(含 t/d 前缀)。</summary>
    public string MailId { get; set; } = string.Empty;

    /// <summary>领取成功的服务端时刻(Unix 毫秒, UTC)。</summary>
    public long ClaimedUnixMs { get; set; }
}

/// <summary>
/// 礼包随机库条目:服务端抽奖用(与客户端 gift_random 同源导出, SV13)。
/// 集合 gift_pool;_id = 行主键 auto_id。同 Index = 一个奖池;按 Rate 权重抽一条得「道具 id × 数量」。
/// 设计 32 读前必看 第 2 条:抽奖在服务端,客户端不申报、不能虚增。
/// </summary>
public sealed class GiftPoolEntryDoc
{
    /// <summary>行主键(对应 gift_random auto_id),作为 _id 主键。</summary>
    [BsonId]
    public int AutoId { get; set; }

    /// <summary>所属礼包 id(同 Index = 一个奖池)。对应 gift_random index。</summary>
    public int Index { get; set; }

    /// <summary>奖品道具 id(指向 item.TbItemDef)。对应 gift_random item_id。</summary>
    public int ItemId { get; set; }

    /// <summary>该奖品数量。对应 gift_random num。</summary>
    public int Num { get; set; }

    /// <summary>权重(抽中概率 = rate / 同 Index 全部 rate 之和)。对应 gift_random rate。</summary>
    public int Rate { get; set; }
}
