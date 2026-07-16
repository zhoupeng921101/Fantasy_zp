using Fantasy.Database;
using Fantasy.Entitas.Interface;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// 兑换码服务端组件初始化:从 Luban 服务端码表(TbRedeemCode)载入码表缓存,并绑定防重记录 / 全局计数的
/// MongoDB 集合句柄。码表是静态配置(独立于 MongoDB 可达性);防并发的原子性依赖 redeem_record 的 _id
/// 主键(MongoDB 主键天然唯一)与 redeem_counter 的条件递增,见 RedeemDecisionHelper。
/// </summary>
public sealed class RedeemServiceComponentAwakeSystem : AwakeSystem<RedeemServiceComponent>
{
    protected override void Awake(RedeemServiceComponent self)
    {
        // 码表来自服务端专用 Luban 配置(TbRedeemCode),载入内存缓存;独立于 MongoDB 可达性。
        // GameConfigSystem.Load 在进程启动期(Entry.Start 之前)完成,此处读表已就绪。
        ReloadCache(self);

        // 防重记录 / 全局计数走 MongoDB 原子操作;不可达时置 null,裁决返 ServiceUnavailable(不本地放行)。
        var database = self.Scene.World.Database;
        if (database?.GetDatabaseInstance is IMongoDatabase mongoDatabase)
        {
            self.Records = mongoDatabase.GetCollection<RedeemRecordDoc>("redeem_record");
            self.Counters = mongoDatabase.GetCollection<RedeemCounterDoc>("redeem_counter");
            // redeem_record 以 _id = "{account}|{code}" 为唯一键,MongoDB 主键天然唯一,
            // 重复插入抛 DuplicateKey,即原子防重的依据(无需额外建唯一索引)。
        }
        else
        {
            // MongoDB 不可达(连接串为空 / 服务未起):记录已兑与计数无法持久,裁决会返「服务不可用」。
            // 属预期环境条件(逻辑就绪、运行依赖外部 MongoDB),用 Warning 不用 Error。
            Log.Warning("RedeemServiceComponent: MongoDB 实例不可用,兑换裁决将返回 ServiceUnavailable。请检查 Fantasy.config 的 <database> 连接串与 MongoDB 可达性。");
        }

        Log.Info($"RedeemServiceComponent 初始化完成,码表缓存条目数={self.CodeCache.Count}");
    }

    /// <summary>
    /// 从 Luban 服务端码表(TbRedeemCode)重新载入码表缓存。供启动与(未来)运营热改后刷新。
    /// key 用服务端规整口径(trim+大写,与裁决入口 RedeemDecisionHelper.Normalize 一致),
    /// 防 Excel 里大小写不一致导致运行时查不到。
    /// </summary>
    public static void ReloadCache(RedeemServiceComponent self)
    {
        self.CodeCache.Clear();
        var table = GameConfigSystem.Tables?.TbRedeemCode;
        if (table == null)
        {
            Log.Error("RedeemServiceComponent: Luban 码表 TbRedeemCode 未加载(GameConfigSystem.Tables 为空或缺表),码表缓存为空,一切兑换将返回 InvalidCode。");
            return;
        }
        foreach (var row in table.DataList)
        {
            self.CodeCache[RedeemDecisionHelper.Normalize(row.Code)] = row;
        }
    }
}

public sealed class RedeemServiceComponentDestroySystem : DestroySystem<RedeemServiceComponent>
{
    protected override void Destroy(RedeemServiceComponent self)
    {
        self.CodeCache.Clear();
        self.Records = null;
        self.Counters = null;
    }
}
