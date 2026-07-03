namespace Fantasy;

/// <summary>
/// 玩家三属性变更来源(服务端审计维度,**非** protobuf 协议字段)。
/// 协议层仍是字符串 reason(守 37 §3.3.2 PropertyChangeRequest 签名不变,boss 硬约束);
/// 服务端在写 ledger 前由 AttrLedgerHelper.MapReasonToSource 把 reason 字符串映射成本枚举,
/// 落 player_attr_ledger.Source 字段供运营按 source 聚合查询。
/// 新增业务路径接 37 ChangeProperty 时,必须在 MapReasonToSource 登记新 reason → 新 source 映射,
/// 否则走 Unknown 兜底(Code Review SV18 ⑤ 拦未登记;运营定期扫 Source=Unknown 排查未登记调用方)。
/// 设计基线:design-docs/44-player-attr-ledger.md §3.3。
/// </summary>
public enum AttrChangeSource
{
    /// <summary>未登记 reason 兜底(reason 为空 / 大小写不匹配 / 未在映射表登记)。</summary>
    Unknown = 0,

    /// <summary>改名扣钻(38 PlayerAttrService → C2G_PropertyChangeRequest,reason = "player_rename")。</summary>
    ChangeNameSpend = 1,

    /// <summary>邮件领奖发货币(32 邮件领奖 → 37 进程内 API,reason 前缀 = "mail_claim")。</summary>
    MailClaim = 2,

    /// <summary>兑换码兑奖发货币(30 兑换码 → 37 进程内 API,reason 前缀 = "redeem_code")。</summary>
    RedeemCode = 3,

    /// <summary>排行榜结算发奖(33 排行榜结算 → 37 进程内 API,reason 前缀 = "rank_settle")。</summary>
    RankSettleReward = 4,

    /// <summary>活动达标发奖(39+40+43 活动 → 37 进程内 API,reason 前缀 = "activity_reward" / "activity_")。</summary>
    ActivityReward = 5,

    // ----- Tier 2+ 增强(留枚举码占位,Tier 2+ 各业务刀接入时登记映射) -----

    /// <summary>玩法消费(Tier 2+ 玩法接入,reason 前缀 = "gameplay_")。</summary>
    GameplayConsume = 6,

    /// <summary>商店购买扣货币(Tier 2+ 商店刀,reason 前缀 = "shop_")。</summary>
    ShopPurchase = 7,

    /// <summary>运营 GM 后台手发(Tier 2+ GM 后台刀,reason 前缀 = "admin_")。</summary>
    AdminGrant = 8,

    /// <summary>退款 / 回滚(Tier 2+ 退款刀,reason 前缀 = "refund_")。</summary>
    Refund = 9,

    /// <summary>使用道具产出货币(背包使用事务 → 折叠 $inc 落货币,reason 前缀 = "item_use")。</summary>
    ItemUse = 10,
}
