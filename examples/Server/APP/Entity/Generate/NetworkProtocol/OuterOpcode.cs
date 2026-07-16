// ReSharper disable InconsistentNaming
namespace Fantasy
{
    /// <summary>
    /// 本代码有编辑器生成,请不要再这里进行编辑。
    /// </summary>
    public static partial class OuterOpcode
    {
        /// <summary> 客户端业务方推累计进度请求(§3.2) 身份从会话取,**不**携带账号字段(协议层即已不预留;即使协议被改坏夹带,handler 也只信会话身份)。 </summary>
        public const uint C2G_ActivityIncrement = 268445457;
        /// <summary> 服务端 Cumulative 累加裁决响应(§3.2) </summary>
        public const uint G2C_ActivityIncrementResponse = 402663185;
        /// <summary> 客户端请求看广告领体力(身份从会话取,不带账号 / 次数——服务端按 AdEnergyConfigServer 派生 + 按 PlayerDoc 判每日闸) </summary>
        public const uint C2G_ClaimAdEnergyRequest = 268445458;
        /// <summary> 服务端看广告领体力裁决响应 </summary>
        public const uint G2C_ClaimAdEnergyResponse = 402663186;
        /// <summary> 客户端请求进入对局(身份从会话取,不携带账号 / 不上传 seed)。 续局语义:服务端有在局(内存活实例或持久 Doc)则恢复、无则新建;消息名沿用 GameStart。 </summary>
        public const uint C2G_GameStartRequest = 268445459;
        /// <summary> 服务端进入对局回带:gameId + seed + 当前候选(新建=首发 / 续局=恢复) + step + score + 完整生成器状态 + 续局标志。 续局时 Step/Score/InitialTrio/Board/GeneratorState 全是恢复出的中断前权威态(非开局 0 态)。 </summary>
        public const uint G2C_GameStartResponse = 402663187;
        /// <summary> 客户端落子请求:只传输入(候选槽位 + 落点),形状服务端权威、不携带 shapeId(反作弊红线) </summary>
        public const uint C2G_PlaceRequest = 268445460;
        /// <summary> 服务端落子裁决 + 最新权威态 </summary>
        public const uint G2C_PlaceResponse = 402663188;
        /// <summary> 客户端消除道具请求:清目标格所在整行整列(脱困道具,设计 49 §3.1)。 只传输入(目标格 + 幂等基准步号),清哪些格 / 消耗多少体力全由服务端权威算,不携带体力值、不携带被清格。 消除道具作为一次 board-mutating 动作推进 Step(与落子同一步号轴),但不消耗候选、不推进发牌调度、不续发新批。 </summary>
        public const uint C2G_ClearToolRequest = 268445461;
        /// <summary> 服务端消除道具裁决 + 最新权威态 </summary>
        public const uint G2C_ClearToolResponse = 402663189;
        /// <summary> 客户端请求当前对局完整快照(重连 / 恢复) </summary>
        public const uint C2G_GameSnapshotRequest = 268445462;
        /// <summary> 服务端回带完整权威态 </summary>
        public const uint G2C_GameSnapshotResponse = 402663190;
        /// <summary> 客户端请求购买体力(身份从会话取,不带账号 / 不带单价 / 不带发放量——服务端自己派生) </summary>
        public const uint C2G_BuyEnergyRequest = 268445463;
        /// <summary> 服务端购买体力裁决响应 </summary>
        public const uint G2C_BuyEnergyResponse = 402663191;
        /// <summary> 客户端请求换装(身份从会话取,不带账号;服务端校验目标已解锁才切换) </summary>
        public const uint C2G_EquipCosmeticRequest = 268445464;
        /// <summary> 服务端换装裁决响应 </summary>
        public const uint G2C_EquipCosmeticResponse = 402663192;
        /// <summary> 客户端上报解锁(client-report:客户端按等级配置算出解锁、上报 id,服务端 sanity 后幂等加入集合) </summary>
        public const uint C2G_UnlockCosmeticRequest = 268445465;
        /// <summary> 服务端解锁上报裁决响应 </summary>
        public const uint G2C_UnlockCosmeticResponse = 402663193;
        /// <summary> 客户端批量上报解锁(一次携带 N 项,身份从会话取) </summary>
        public const uint C2G_UnlockCosmeticBatchRequest = 268445466;
        /// <summary> 服务端批量解锁裁决响应(回带处理后两个 kind 的最终解锁集) </summary>
        public const uint G2C_UnlockCosmeticBatchResponse = 402663194;
        /// <summary> 客户端进入主游戏(登录后、主游戏可交互前发起;每次进入主游戏阶段调用一次) </summary>
        public const uint C2G_EnterMainGameRequest = 268445467;
        /// <summary> 服务端对进入主游戏请求的响应:订单快照 + 道具持有 + 塔罗进度 </summary>
        public const uint G2C_EnterMainGameResponse = 402663195;
        /// <summary> 客户端登陆到Gate服务器 </summary>
        public const uint C2G_LoginGameRequest = 268445468;
        public const uint G2C_LoginGameResponse = 402663196;
        /// <summary> 客户端请求领取女神满档奖励(身份从会话取,不带账号 / 不带奖励内容——服务端按订单态 + 表自定) </summary>
        public const uint C2G_GoddessClaimRequest = 268445469;
        /// <summary> 服务端女神领取裁决响应 </summary>
        public const uint G2C_GoddessClaimResponse = 402663197;
        /// <summary> 客户端上报女神奖励盘面溢出、请求入库(元素道具按数量落 ItemBag;身份从会话取,不带账号——反作弊红线) </summary>
        public const uint C2G_GoddessOverflow = 268445470;
        /// <summary> 溢出入库裁决响应 </summary>
        public const uint G2C_GoddessOverflowResponse = 402663198;
        /// <summary> 客户端玩法界面双击背包元素道具、请求取回一个到盘面(服务端权威 -1;身份从会话取) </summary>
        public const uint C2G_RetrieveElement = 268445471;
        /// <summary> 取回裁决响应(成功后客户端再把元素飞回盘面) </summary>
        public const uint G2C_RetrieveElementResponse = 402663199;
        /// <summary> 客户端发起使用道具请求(身份从会话取,不携带账号)。 reqSeq:客户端本地单调递增序号,同一次使用重发用同一 reqSeq,服务端据此幂等去重(防弱网重发双扣)。 契约(客户端必守):reqSeq 全账号单一单调计数器(跨道具共用,非每道具独立);且串行发号—— 一次使用等其响应回来再发下一个,不并发/乱序投递。服务端幂等锚 LastUseReqSeq 是单标量, 一个较小 reqSeq 晚于较大者到达会被判 Duplicate(该次使用不生效);Duplicate 分支回推权威背包兜底对账。 </summary>
        public const uint C2G_UseItem = 268445472;
        /// <summary> 服务端使用道具裁决响应。 </summary>
        public const uint G2C_UseItemResponse = 402663200;
        /// <summary> 服务端背包变更主动推送(获得 / 使用 / 过期后起,推送目标 = 该 UUID 在线全部会话)。 语义:整份当前背包(两轨全量),客户端直接覆盖本地投影。Loaded=true 恒成立(推送仅在写库成功、持有权威文档时起), 客户端据 Loaded 区分「权威整份」与降级空占位(proto3 repeated 无法区分空集与缺失,防误清)。 丢失 = 下次登录拉快照对齐(不重试);离线 = 丢弃。 </summary>
        public const uint G2C_InventoryDeltaPush = 134227729;
        /// <summary> 客户端拉邮件列表请求（无业务字段:身份从会话取,不携带账号;触发时机由客户端定）（§3.1） </summary>
        public const uint C2G_MailListRequest = 268445473;
        /// <summary> 服务端拉列表响应（该账号应收、未过期的邮件 + 每封领取态）（§3.2） </summary>
        public const uint G2C_MailListResponse = 402663201;
        /// <summary> 客户端领取一封邮件请求（身份从会话取，不携带账号）（§3.3） </summary>
        public const uint C2G_MailClaimRequest = 268445474;
        /// <summary> 服务端领取裁决响应（结果码 + 成功时奖励列表）（§3.4） </summary>
        public const uint G2C_MailClaimResponse = 402663202;
        /// <summary> 客户端请求交付某槽位的订单(身份从会话取,不带账号 / 不带订单类型 / 不带奖励金额——服务端按 OrderCursor + DeliveredMask 自己定) </summary>
        public const uint C2G_DeliverOrderRequest = 268445475;
        /// <summary> 服务端交付裁决响应 </summary>
        public const uint G2C_DeliverOrderResponse = 402663203;
        public const uint C2G_TestEmptyMessage = 134227730;
        public const uint C2G_TestMessage = 134227731;
        public const uint C2G_TestRequest = 268445476;
        public const uint G2C_TestResponse = 402663204;
        public const uint C2G_TestRequestPushMessage = 134227732;
        /// <summary> Gate服务器推送一个消息给客户端 </summary>
        public const uint G2C_PushMessage = 134227733;
        public const uint C2G_CreateAddressableRequest = 268445477;
        public const uint G2C_CreateAddressableResponse = 402663205;
        public const uint C2M_TestMessage = 1342187281;
        public const uint C2M_TestRequest = 1476405009;
        public const uint M2C_TestResponse = 1610622737;
        /// <summary> 通知Gate服务器创建一个Chat的Route连接 </summary>
        public const uint C2G_CreateChatRouteRequest = 268445478;
        public const uint G2C_CreateChatRouteResponse = 402663206;
        /// <summary> 发送一个Route消息给Chat </summary>
        public const uint C2Chat_TestMessage = 2147493649;
        /// <summary> 发送一个RPCRoute消息给Chat </summary>
        public const uint C2Chat_TestMessageRequest = 2281711377;
        public const uint Chat2C_TestMessageResponse = 2415929105;
        /// <summary> 发送一个RPC消息给Map，让Map里的Entity转移到另外一个Map上 </summary>
        public const uint C2M_MoveToMapRequest = 1476405010;
        public const uint M2C_MoveToMapResponse = 1610622738;
        /// <summary> 发送一个消息给Gate，让Gate发送一个Addressable消息给MAP </summary>
        public const uint C2G_SendAddressableToMap = 134227734;
        /// <summary> 发送一个消息给Chat，让Chat服务器主动推送一个RouteMessage消息给客户端 </summary>
        public const uint C2Chat_TestRequestPushMessage = 2147493650;
        /// <summary> Chat服务器主动推送一个消息给客户端 </summary>
        public const uint Chat2C_PushMessage = 2147493651;
        /// <summary> 客户端发送给Gate服务器通知map服务器创建一个SubScene </summary>
        public const uint C2G_CreateSubSceneRequest = 268445479;
        public const uint G2C_CreateSubSceneResponse = 402663207;
        /// <summary> 客户端通知Gate服务器给SubScene发送一个消息 </summary>
        public const uint C2G_SendToSubSceneMessage = 134227735;
        /// <summary> 客户端通知Gate服务器创建一个SubScene的Address消息 </summary>
        public const uint C2G_CreateSubSceneAddressableRequest = 268445480;
        public const uint G2C_CreateSubSceneAddressableResponse = 402663208;
        /// <summary> 客户端向SubScene发送一个测试消息 </summary>
        public const uint C2SubScene_TestMessage = 1342187282;
        /// <summary> 客户端向SubScene发送一个销毁测试消息 </summary>
        public const uint C2SubScene_TestDisposeMessage = 1342187283;
        /// <summary> 客户端向服务器发送连接消息（Roaming） </summary>
        public const uint C2G_ConnectRoamingRequest = 268445481;
        public const uint G2C_ConnectRoamingResponse = 402663209;
        /// <summary> 测试一个Chat漫游普通消息 </summary>
        public const uint C2Chat_TestRoamingMessage = 2550146833;
        /// <summary> 测试一个Map漫游普通消息 </summary>
        public const uint C2Map_TestRoamingMessage = 2550146834;
        /// <summary> 测试一个Chat漫游RPC消息 </summary>
        public const uint C2Chat_TestRPCRoamingRequest = 2684364561;
        public const uint Chat2C_TestRPCRoamingResponse = 2818582289;
        /// <summary> 客户端发送一个漫游消息给Map通知Map主动推送一个消息给客户端 </summary>
        public const uint C2Map_PushMessageToClient = 2550146835;
        /// <summary> 漫游端发送一个消息给客户端 </summary>
        public const uint Map2C_PushMessageToClient = 2550146836;
        /// <summary> 测试传送漫游的触发协议 </summary>
        public const uint C2Map_TestTransferRequest = 2684364562;
        public const uint Map2C_TestTransferResponse = 2818582290;
        /// <summary> 测试一个Chat发送到Map之间漫游协议 </summary>
        public const uint C2Chat_TestSendMapMessage = 2550146837;
        /// <summary> 通知Gate服务器发送一个Route消息给Map的漫游终端 </summary>
        public const uint C2G_TestRouteToRoaming = 134227736;
        /// <summary> 通知Gate服务器发送一个漫游消息给Map的漫游终端 </summary>
        public const uint C2G_TestRoamingToRoaming = 134227737;
        /// <summary> 客户端向服务器发送登录连接消息（Roaming） </summary>
        public const uint C2G_LoginRoamingRequest = 268445482;
        public const uint G2C_LoginRoamingResponse = 402663210;
        /// <summary> 通知Gate服务器发送一个内网消息通知Map服务器向Gate服务器注册一个领域事件 </summary>
        public const uint C2G_SubscribeSphereEventRequest = 268445483;
        public const uint G2C_SubscribeSphereEventResponse = 402663211;
        /// <summary> 通知Gate发送一个订阅领域事件 </summary>
        public const uint C2G_PublishSphereEventRequest = 268445484;
        public const uint G2C_PublishSphereEventResponse = 402663212;
        /// <summary> 通知Gate取消一个订阅领域事件 </summary>
        public const uint C2G_UnsubscribeSphereEventRequest = 268445485;
        public const uint G2C_UnsubscribeSphereEventResponse = 402663213;
        /// <summary> 通知Map取消一个订阅领域事件 </summary>
        public const uint C2G_MapUnsubscribeSphereEventRequest = 268445486;
        public const uint G2C_MapUnsubscribeSphereEventResponse = 402663214;
        public const uint C2G_TestMemoryPackRequest = 285222703;
        public const uint G2C_TestMemoryPackResponse = 419440431;
        /// <summary> 服务端登录后下发玩家信息整份快照(主动 push,取代 G2C_PropertyInitSnapshot) </summary>
        public const uint G2C_PlayerInfoSnapshot = 134227738;
        /// <summary> 客户端发起属性变更声明请求(只声明相对增量 + 原因,身份从会话取)(§3.3.2) </summary>
        public const uint C2G_PropertyChangeRequest = 268445488;
        /// <summary> 服务端属性变更裁决响应(§3.3.2) </summary>
        public const uint G2C_PropertyChangeResponse = 402663216;
        /// <summary> 服务端属性变更主动推送(每次写库成功后服务端起,推送目标 = 该 UUID 在线全部会话,§3.3.3 + §5.4) 推送是「绝对余额快照」非「相对变更流水」,丢失 = 下次登录拉快照对齐(O6 不重试) </summary>
        public const uint G2C_PropertyDeltaPush = 134227739;
        /// <summary> 客户端批量属性变更请求(一次携带 N 项,身份从会话取) </summary>
        public const uint C2G_PropertyBatchChangeRequest = 268445489;
        /// <summary> 服务端批量变更逐项裁决响应(Results 顺序与请求 Items 一一对应;客户端也按 Type 匹配双保险) </summary>
        public const uint G2C_PropertyBatchChangeResponse = 402663217;
        /// <summary> 客户端拉 ledger 流水请求(身份从会话取,不携带账号)(§3.1) limit 必填(本子单不设默认);服务端钳制 [0, 100],超上限钳为 100 不报错(降级语义)。 </summary>
        public const uint C2G_QueryAttrLedger = 268445490;
        /// <summary> 服务端查询响应(§3.2) </summary>
        public const uint G2C_QueryAttrLedgerResponse = 402663218;
        /// <summary> 客户端全量上报三态(身份从会话取,不带账号;SET 语义,服务端存客户端设的值 + sanity) </summary>
        public const uint C2G_SetProfileStateRequest = 268445491;
        /// <summary> 服务端设置三态裁决响应(回带当前权威三态供客户端对账) </summary>
        public const uint G2C_SetProfileStateResponse = 402663219;
        /// <summary> 客户端上报一次成绩请求（玩法结束提交；身份从会话取，不携带账号）（§3.1） </summary>
        public const uint C2G_RankSubmitScoreRequest = 268445492;
        /// <summary> 服务端上报裁决响应（结果码 + 当前最佳）（§3.2） </summary>
        public const uint G2C_RankSubmitScoreResponse = 402663220;
        /// <summary> 客户端查榜请求（不分页，返展示上限条数；身份从会话取，不携带账号）（§3.4） </summary>
        public const uint C2G_RankQueryRequest = 268445493;
        /// <summary> 服务端查榜响应（前 N 名条目 + 自己名次 + 自己分数）（§3.5） </summary>
        public const uint G2C_RankQueryResponse = 402663221;
        /// <summary> 客户端领取每日/点赞奖请求（身份从会话取，不携带账号） </summary>
        public const uint C2G_RankClaimRewardRequest = 268445503;
        /// <summary> 服务端领取裁决响应（奖励走服务端邮件，不在回包；客户端成功后重拉收件箱见奖） </summary>
        public const uint G2C_RankClaimRewardResponse = 402663231;
        /// <summary> 客户端提交兑换码请求（玩家身份从会话取，不在请求中携带账号） </summary>
        public const uint C2G_RedeemCodeRequest = 268445494;
        /// <summary> 服务端兑换裁决响应 </summary>
        public const uint G2C_RedeemCodeResponse = 402663222;
        /// <summary> 客户端请求改名(身份从会话取,不带账号 / 不带费用 / 不带次数——服务端按 PlayerDoc.RenameCount 自己算) </summary>
        public const uint C2G_RenameRequest = 268445495;
        /// <summary> 服务端改名裁决响应 </summary>
        public const uint G2C_RenameResponse = 402663223;
        /// <summary> 客户端请求购买一步塔罗牌进度(身份从会话取;本步成本由服务端按 TbTarotCard.UnlockCosts[当前步] 自算,客户端不上报) </summary>
        public const uint C2G_TarotPurchaseRequest = 268445496;
        /// <summary> 服务端购买裁决响应 </summary>
        public const uint G2C_TarotPurchaseResponse = 402663224;
        /// <summary> 测试使用ErrorCode枚举的消息 </summary>
        public const uint C2G_TestEnumMessage = 134227740;
        /// <summary> 客户端请求祈愿兑体力(身份从会话取,不带账号 / 费用 / 次数——服务端按 WishConfigServer 派生 + 按 PlayerDoc 判每日闸) </summary>
        public const uint C2G_WishForEnergyRequest = 268445497;
        /// <summary> 服务端祈愿裁决响应 </summary>
        public const uint G2C_WishForEnergyResponse = 402663225;
        /// <summary> 客户端请求取用溢出储存(身份从会话取,不带账号 / 不带数量——服务端把整池尽量转入体力) </summary>
        public const uint C2G_WithdrawOverflowRequest = 268445498;
        /// <summary> 服务端取用溢出储存裁决响应 </summary>
        public const uint G2C_WithdrawOverflowResponse = 402663226;
        /// <summary> 客户端请求清空自己的玩家数据(身份从会话取,不携带 playerId / 不接受指定清别人) </summary>
        public const uint C2G_ClearPlayerDataRequest = 268445499;
        /// <summary> 服务端清档裁决响应 </summary>
        public const uint G2C_ClearPlayerDataResponse = 402663227;
        /// <summary> 客户端 GM 清盘请求(身份从会话取,不携带账号) </summary>
        public const uint C2G_ClearBoardRequest = 268445500;
        /// <summary> 服务端清盘裁决 + 最新权威态 </summary>
        public const uint G2C_ClearBoardResponse = 402663228;
        /// <summary> 客户端 GM 清背包请求(身份从会话取,不携带账号) </summary>
        public const uint C2G_ClearInventoryRequest = 268445501;
        /// <summary> 服务端清背包裁决响应 </summary>
        public const uint G2C_ClearInventoryResponse = 402663229;
        /// <summary> 客户端 GM 发测试道具请求(身份从会话取,不携带账号) </summary>
        public const uint C2G_GrantTestItemRequest = 268445502;
        /// <summary> 服务端发测试道具裁决响应 </summary>
        public const uint G2C_GrantTestItemResponse = 402663230;
    }
}