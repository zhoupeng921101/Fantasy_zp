using System;
using Fantasy.Network;
using Fantasy.ProtocolExportTool.Models;

namespace Fantasy.ProtocolExportTool.Generators;

/// <summary>
/// OpCode 生成器 - 为消息生成唯一的 OpCode。
/// 计数器经 OpCodeLock 保号分配(已记录复用、新增取 max+1、删除留坟不复用),
/// 不再使用内存递增计数器——编号与文件扫描顺序解耦。
/// </summary>
public sealed class OpCodeGenerator(bool isOuter, OpCodeLock opCodeLock)
{
    /// <summary>
    /// 与历史递增分配的起始值一致(ProtocolOpCode 时代的 Start),空账本首次导出的编号与旧逻辑逐一相同。
    /// </summary>
    private const uint Start = 10001;

    private readonly string _side = isOuter ? "Outer" : "Inner";

    /// <summary>
    /// 为消息生成 OpCode
    /// </summary>
    public OpcodeInfo Generate(MessageDefinition message)
    {
        var (protocolType, category) = GetProtocolTypeAndCategory(message.InterfaceType);
        var counter = opCodeLock.Acquire(_side, category, message.Name, Start);

        return new OpcodeInfo
        {
            Name = message.Name,
            ProtocolType = message.Protocol.OpCodeType,
            Comment = string.Join(" ", message.DocumentationComments),
            Code = OpCode.Create(message.Protocol.OpCodeType, protocolType, counter)
        };
    }

    /// <summary>
    /// 获取协议类型与账本类别(类别 = 独立编号空间,与旧实现中各计数器字段一一对应)
    /// </summary>
    private (uint protocolType, string category) GetProtocolTypeAndCategory(string interfaceType)
    {
        return interfaceType switch
        {
            "IMessage" when isOuter => (OpCodeType.OuterMessage, "Message"),
            "IMessage" => (OpCodeType.InnerMessage, "Message"),

            "IRequest" when isOuter => (OpCodeType.OuterRequest, "Request"),
            "IRequest" => (OpCodeType.InnerRequest, "Request"),

            "IResponse" when isOuter => (OpCodeType.OuterResponse, "Response"),
            "IResponse" => (OpCodeType.InnerResponse, "Response"),

            "IAddressMessage" when !isOuter => (OpCodeType.InnerAddressMessage, "AddressMessage"),
            "IAddressRequest" when !isOuter => (OpCodeType.InnerAddressRequest, "AddressRequest"),
            "IAddressResponse" when !isOuter => (OpCodeType.InnerAddressResponse, "AddressResponse"),

            "IAddressableMessage" when isOuter => (OpCodeType.OuterAddressableMessage, "AddressableMessage"),
            "IAddressableMessage" => (OpCodeType.InnerAddressableMessage, "AddressableMessage"),

            "IAddressableRequest" when isOuter => (OpCodeType.OuterAddressableRequest, "AddressableRequest"),
            "IAddressableRequest" => (OpCodeType.InnerAddressableRequest, "AddressableRequest"),

            "IAddressableResponse" when isOuter => (OpCodeType.OuterAddressableResponse, "AddressableResponse"),
            "IAddressableResponse" => (OpCodeType.InnerAddressableResponse, "AddressableResponse"),

            "ICustomRouteMessage" when isOuter => (OpCodeType.OuterCustomRouteMessage, "CustomRouteMessage"),
            "ICustomRouteRequest" when isOuter => (OpCodeType.OuterCustomRouteRequest, "CustomRouteRequest"),
            "ICustomRouteResponse" when isOuter => (OpCodeType.OuterCustomRouteResponse, "CustomRouteResponse"),

            "IRoamingMessage" when isOuter => (OpCodeType.OuterRoamingMessage, "RoamingMessage"),
            "IRoamingMessage" => (OpCodeType.InnerRoamingMessage, "RoamingMessage"),

            "IRoamingRequest" when isOuter => (OpCodeType.OuterRoamingRequest, "RoamingRequest"),
            "IRoamingRequest" => (OpCodeType.InnerRoamingRequest, "RoamingRequest"),

            "IRoamingResponse" when isOuter => (OpCodeType.OuterRoamingResponse, "RoamingResponse"),
            "IRoamingResponse" => (OpCodeType.InnerRoamingResponse, "RoamingResponse"),

            _ => throw new InvalidOperationException($"Unsupported interface type: {interfaceType}")
        };
    }
}
