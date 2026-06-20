using MongoDB.Bson.Serialization.Attributes;

namespace Fantasy;

// 账号账本的原生 MongoDB 文档定义(非框架 Entity)。
// 首连自动注册 / 重连仅刷新末次登录时间,需要 MongoDB 单条原子 upsert(查 + 写一步)防并发同 UUID 双登。
// 框架 IDatabase 高层 API 只能先读后写(同邮件 / 兑换 / 排行榜先例,设计明令禁止),故直接用原生 IMongoDatabase + BSON 文档。
// 设计基线:design-docs/35-account-server.md §3.1。

/// <summary>
/// 账号账本文档:每 UUID 一行,记首次注册时间 / 末次登录时间 / 状态。
/// 集合 accounts;_id = AccountId(客户端 PlayerPrefs 设备 UUID,与既有四特性 account 字段同源,SV10)。
/// 主键 MongoDB 天然唯一,无需额外索引(本子单无按时间查询需求,Tier 1+ 真要再加,SV2)。
/// 状态字段本子单不消费(SV5):upsert 的 $setOnInsert 仅在 insert 时写默认 0,update 路径完全不触碰
/// (Tier 1+ 运营后台改写封禁后,玩家再登不会被本子单重置)。
/// </summary>
public sealed class AccountDoc
{
    /// <summary>账号 id(= 客户端 PlayerPrefs 设备 UUID 字符串,裸 UUID 不哈希不加盐),作为 _id 主键。</summary>
    [BsonId]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>首次注册时间(服务端 Unix 毫秒,UTC)。insert 时由 $setOnInsert 写入,后续 update 路径不动(SV4)。</summary>
    public long FirstLoginUnixMs { get; set; }

    /// <summary>末次登录时间(服务端 Unix 毫秒,UTC)。每次登录 $set 刷新(SV3/SV4)。</summary>
    public long LastLoginUnixMs { get; set; }

    /// <summary>账号状态(0 = 正常,默认值)。本子单只让字段先有 / 不消费 / 不误改;Tier 1+ 运营后台再消费。</summary>
    public int Status { get; set; }
}
