using System;
using System.Collections.Generic;
using Fantasy.Async;
using Fantasy.Database;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 兑换码服务端组件初始化:绑定原生 MongoDB 集合句柄、建唯一索引、载入码表缓存、首次启动播种示例码表。
/// 防并发的原子性依赖 redeem_record 的 _id 主键(MongoDB 主键天然唯一)与 redeem_counter 的条件递增,
/// 见 RedeemDecisionHelper。
/// </summary>
public sealed class RedeemServiceComponentAwakeSystem : AwakeSystem<RedeemServiceComponent>
{
    /// <summary>MongoDB 重复键错误码(与 RedeemDecisionHelper 同口径)。</summary>
    private const int DuplicateKeyErrorCode = 11000;

    protected override void Awake(RedeemServiceComponent self)
    {
        // 初始化放协程里执行(AwakeSystem 本身是同步签名),失败不阻断 Scene 创建。
        Init(self).Coroutine();
    }

    private static async FTask Init(RedeemServiceComponent self)
    {
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is not IMongoDatabase mongoDatabase)
        {
            // MongoDB 不可达(连接串为空 / 服务未起):记录已兑与计数无法持久,裁决会返「服务不可用」。
            // 这对应交接区 BLOCKED-环境:逻辑就绪、运行依赖外部 MongoDB。属预期环境条件,用 Warning 不用 Error。
            Log.Warning("RedeemServiceComponent: MongoDB 实例不可用,兑换裁决将返回 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
            return;
        }

        var codeTable = mongoDatabase.GetCollection<RedeemCodeDoc>("redeem_code");
        self.CodeTable = codeTable;
        self.Records = mongoDatabase.GetCollection<RedeemRecordDoc>("redeem_record");
        self.Counters = mongoDatabase.GetCollection<RedeemCounterDoc>("redeem_counter");

        // redeem_record 以 _id = "{account}|{code}" 为唯一键,MongoDB 主键天然唯一,
        // 重复插入抛 DuplicateKey,即原子防重的依据(无需额外建唯一索引)。

        // 首次启动播种示例码表(已存在则跳过,不覆盖运营改动)。
        await SeedSampleCodes(codeTable);

        // 载入码表到内存缓存(只读裁决用,权威防重/计数始终走 MongoDB)。
        await ReloadCache(self);

        Log.Info($"RedeemServiceComponent 初始化完成,码表缓存条目数={self.CodeCache.Count}");
    }

    /// <summary>
    /// 重新载入码表缓存。供启动与(未来)运营热改后刷新。
    /// </summary>
    public static async FTask ReloadCache(RedeemServiceComponent self)
    {
        if (self.CodeTable == null)
        {
            return;
        }
        self.CodeCache.Clear();
        var all = await self.CodeTable.Find(FilterDefinition<RedeemCodeDoc>.Empty).ToListAsync();
        foreach (var doc in all)
        {
            self.CodeCache[doc.Code] = doc;
        }
    }

    /// <summary>
    /// 播种示例码表,仅当对应码不存在时插入(不覆盖运营已配置/已改的码)。
    /// 覆盖 SV1/SV4/SV5 各分支的可验证样例码。
    /// 幂等性靠 _id(Code)主键唯一保证:重复插入抛 DuplicateKey 即「已播种 / 已存在」,捕获后跳过续插。
    /// 这使播种在「多 Gate Scene 并发首启」与「服务端重启」两种场景都安全
    /// (不做先读后写的存在性预检——那是 check-then-act 竞态,并发两个 Gate 可同时通过预检再各自插入)。
    /// </summary>
    private static async FTask SeedSampleCodes(IMongoCollection<RedeemCodeDoc> codeTable)
    {
        var samples = new List<RedeemCodeDoc>
        {
            // SV1:有效码,无过期、不限量,首次兑换返成功 + 权威发放奖励盒 7002(命运能量x5 + 钻石x50)。
            new RedeemCodeDoc
            {
                Code = "WELCOME2026",
                RewardBoxId = 7002,
                ExpireUnixMs = 0,
                GlobalLimit = 0
            },
            // SV4:已过期码(过期时间设为很早的 Unix 毫秒);奖励盒任取(过期不会发)。
            new RedeemCodeDoc
            {
                Code = "EXPIRED2020",
                RewardBoxId = 7001,
                ExpireUnixMs = 1577836800000, // 2020-01-01 UTC
                GlobalLimit = 0
            },
            // SV5/SV8:限量码,全局上限 3,用于验证达上限返「全局限量已满」与并发不超发。
            new RedeemCodeDoc
            {
                Code = "LIMITED3",
                RewardBoxId = 7001,
                ExpireUnixMs = 0,
                GlobalLimit = 3
            }
        };

        foreach (var sample in samples)
        {
            // 直接插入,以 _id(Code) 主键唯一作为幂等依据:
            //   - 该码尚未存在 → 插入成功(首启播种)。
            //   - 该码已存在(并发另一 Gate 已插 / 服务端重启已在库)→ DuplicateKey,捕获后跳过、继续下一个,不中断 Init。
            // 只吞「已存在」这一种(DuplicateKey 11000);其他 Mongo 异常不在此捕获,照常向上抛(不掩盖真实故障)。
            try
            {
                await codeTable.InsertOneAsync(sample);
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

public sealed class RedeemServiceComponentDestroySystem : DestroySystem<RedeemServiceComponent>
{
    protected override void Destroy(RedeemServiceComponent self)
    {
        self.CodeCache.Clear();
        self.CodeTable = null;
        self.Records = null;
        self.Counters = null;
    }
}
