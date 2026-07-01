namespace Fantasy;

/// <summary>
/// 改名费用服务端权威镜像(云存档 blob 退役·第 2 批·子批 2a)。
///
/// 单源原则(参 MergeOrderConfigServer 镜像手法):改名价是客户端编译期常量、无 Luban 表 id,
/// 故服务端同样固定常量镜像。改客户端 RenamePriceConfig.RENAME_PRICE 时须同步改此处。
///
/// 计费口径必须与客户端 RenamePriceConfig 一致:首次免费(RenameCount==0),之后每次 PriceFor(RenameCount)。
/// 本轮客户端为固定价(不分档),服务端同为固定价;后续分档时两端同步查表逻辑。
/// </summary>
public static class RenameConfigServer
{
    /// <summary>固定改名价(钻石,= 客户端 RenamePriceConfig.RENAME_PRICE)。首次免费,之后每次同价。</summary>
    public const int RenamePrice = 100;

    /// <summary>
    /// 昵称最大长度(= 客户端 PlayerRenameService.MaxLen)。服务端基本 sanity:超长拒。
    /// 客户端已做完整合法性 + 屏蔽字校验;服务端本批只挡长度/空串,屏蔽字留后续加固。
    /// </summary>
    public const int MaxNicknameLength = 16;

    /// <summary>
    /// 按已改名次数取改名费(= 客户端 RenamePriceConfig.PriceFor)。
    /// RenameCount==0 → 0(首次免费);否则固定价。分档时改此处查表。
    /// </summary>
    public static int PriceFor(int renameCount) => renameCount == 0 ? 0 : RenamePrice;
}
