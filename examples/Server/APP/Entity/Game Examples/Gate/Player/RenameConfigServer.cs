namespace Fantasy;

/// <summary>
/// 改名费用服务端权威取值(云存档 blob 退役·第 2 批·子批 2a)。
///
/// 单源原则:改名价读 Luban global.xlsx id=8(GlobalCfg.RenamePrice),与客户端 RenamePriceConfig 双端同源;
/// 缺表/缺键回退 DefaultRenamePrice。改价改 global.xlsx 重跑 gen 即双端生效,无需改代码。
///
/// 计费口径必须与客户端 RenamePriceConfig 一致:首次免费(RenameCount==0),之后每次 PriceFor(RenameCount)。
/// 本轮不分档(单一价);后续分档时两端同步查表逻辑。
/// </summary>
public static class RenameConfigServer
{
    /// <summary>改名价回退默认(钻石)。global.xlsx id=8 缺表/缺键时用此值,= 配置初值 100。</summary>
    private const int DefaultRenamePrice = 100;

    /// <summary>
    /// 改名价(钻石)。启动期读 Luban global.xlsx id=8 一次缓存,缺表/缺键回退 DefaultRenamePrice
    /// (与姊妹类 MergeOrderConfigServer 同款 static readonly 缓存范式;GameConfigSystem.Load 幂等一次性、无热重载,无需每次现读)。
    /// </summary>
    public static readonly int Price = GlobalCfg.GetInt(GlobalCfg.RenamePrice, DefaultRenamePrice);

    /// <summary>
    /// 昵称最大长度(= 客户端 PlayerRenameService.MaxLen)。服务端基本 sanity:超长拒。
    /// (屏蔽字终审见 RenameHelper ①.5 ProfanityFilterServer,与长度 sanity 并列。)
    /// </summary>
    public const int MaxNicknameLength = 16;

    /// <summary>
    /// 按已改名次数取改名费(= 客户端 RenamePriceConfig.PriceFor)。
    /// RenameCount==0 → 0(首次免费);否则取缓存 Price(启动期读 global.xlsx id=8)。分档时改此处查表。
    /// </summary>
    public static int PriceFor(int renameCount) => renameCount == 0 ? 0 : Price;
}
