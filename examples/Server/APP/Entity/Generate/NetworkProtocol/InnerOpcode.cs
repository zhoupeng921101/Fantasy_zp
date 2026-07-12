// ReSharper disable InconsistentNaming
namespace Fantasy
{
    /// <summary>
    /// 本代码有编辑器生成,请不要再这里进行编辑。
    /// </summary>
    public static partial class InnerOpcode
    {
        public const uint G2A_TestMessage = 939534097;
        public const uint G2A_TestRequest = 1073751825;
        public const uint G2A_TestResponse = 1207969553;
        public const uint G2M_RequestAddressableId = 1073751826;
        public const uint M2G_ResponseAddressableId = 1207969554;
        /// <summary> 通知Chat服务器创建一个RouteId </summary>
        public const uint G2Chat_CreateRouteRequest = 1073751827;
        public const uint Chat2G_CreateRouteResponse = 1207969555;
        /// <summary> Map给另外一个Map发送Unit数据 </summary>
        public const uint M2M_SendUnitRequest = 1090529044;
        public const uint M2M_SendUnitResponse = 1224746772;
        /// <summary> Gate发送Addressable消息给MAP </summary>
        public const uint G2M_SendAddressableMessage = 1744840465;
        public const uint G2M_CreateSubSceneRequest = 1073751829;
        public const uint M2G_CreateSubSceneResponse = 1207969557;
        public const uint G2SubScene_SentMessage = 939534098;
        /// <summary> Gate通知SubScene创建一个Addressable消息 </summary>
        public const uint G2SubScene_AddressableIdRequest = 1073751830;
        public const uint SubScene2G_AddressableIdResponse = 1207969558;
        /// <summary> Chat发送一个漫游消息给Map </summary>
        public const uint Chat2M_TestMessage = 2952800017;
        /// <summary> 测试一个Gate服务器发送一个Route消息给某个漫游终端 </summary>
        public const uint G2Map_TestRouteMessageRequest = 1073751831;
        public const uint Map2G_TestRouteMessageResponse = 1207969559;
        /// <summary> 测试一个Gate服务器发送一个漫游协议给某个漫游终端 </summary>
        public const uint G2Map_TestRoamingMessageRequest = 3087017745;
        public const uint Map2G_TestRoamingMessageResponse = 3221235473;
        /// <summary> Gate服务器通知Map订阅一个领域事件到Gate上 </summary>
        public const uint G2Map_SubscribeSphereEventRequest = 1073751832;
        public const uint G2Map_SubscribeSphereEventResponse = 1207969560;
        /// <summary> Gate服务器通知Map取消订阅一个领域事件到Gate上 </summary>
        public const uint G2Map_UnsubscribeSphereEventRequest = 1073751833;
        public const uint Map2G_UnsubscribeSphereEventResponse = 1207969561;
        /// <summary> GM 给指定账号发道具(限时/可堆叠由服务端按配置自动分轨) </summary>
        public const uint Http2G_GmGrantItemRequest = 1073751834;
        public const uint G2Http_GmGrantItemResponse = 1207969562;
        /// <summary> GM 给指定账号加减货币(高信任通道,单次幅度上限由 Gate 侧校验) </summary>
        public const uint Http2G_GmChangeCurrencyRequest = 1073751835;
        public const uint G2Http_GmChangeCurrencyResponse = 1207969563;
        /// <summary> GM 给指定账号投一封带奖励的定向邮件 </summary>
        public const uint Http2G_GmSendMailRequest = 1073751836;
        public const uint G2Http_GmSendMailResponse = 1207969564;
        /// <summary> GM 重置指定账号(清空背包 + 邮件数据) </summary>
        public const uint Http2G_GmResetPlayerRequest = 1073751837;
        public const uint G2Http_GmResetPlayerResponse = 1207969565;
    }
}