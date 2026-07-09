using System;
using System.Runtime.CompilerServices;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Network.WebSocket;
using Fantasy.PacketParser.Interface;
// ReSharper disable PossibleNullReferenceException
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
namespace Fantasy.PacketParser
{
    public static class PacketParserFactory
    {
#if FANTASY_NET
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static APacketParser CreatePacketParser(NetworkTarget  networkTarget)
        {
            return CreatePacketParser(ProgramDefine.InnerNetwork, networkTarget);
        }
#endif
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static APacketParser CreatePacketParser(NetworkProtocolType networkProtocolType, NetworkTarget networkTarget)
        {
            switch (networkProtocolType)
            {
                case NetworkProtocolType.KCP:
                {
                    return CreateBufferPacketParser(networkTarget);
                }
                case NetworkProtocolType.TCP:
                {
                    return CreateReadOnlyMemoryPacketParser(networkTarget);
                }
                case NetworkProtocolType.WebSocket:
                {
                    return CreateWebglBufferPacketParser(networkTarget);
                }
                default:
                {
                    throw new NotImplementedException($"NetworkProtocolType:{networkProtocolType.ToString()} Not supported");
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlyMemoryPacketParser CreateReadOnlyMemoryPacketParser(ANetwork network)
        {
            var readOnlyMemoryPacketParser = CreateReadOnlyMemoryPacketParser(network.NetworkTarget);
            readOnlyMemoryPacketParser.Scene = network.Scene;
            readOnlyMemoryPacketParser.Network = network;
            readOnlyMemoryPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return readOnlyMemoryPacketParser;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BufferPacketParser CreateBufferPacketParser(ANetwork network)
        {
            var bufferPacketParser = CreateBufferPacketParser(network.NetworkTarget);
            bufferPacketParser.Scene = network.Scene;
            bufferPacketParser.Network = network;
            bufferPacketParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return bufferPacketParser;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static APacketParser CreateWebglBufferPacketParser(ANetwork network)
        {
            var packetParser = CreateWebglBufferPacketParser(network.NetworkTarget);
            packetParser.Scene = network.Scene;
            packetParser.Network = network;
            packetParser.MessageDispatcherComponent = network.Scene.MessageDispatcherComponent;
            return packetParser;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlyMemoryPacketParser CreateReadOnlyMemoryPacketParser(NetworkTarget networkTarget)
        {
            switch (networkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    return new InnerReadOnlyMemoryPacketParser();
                }
#endif
                case NetworkTarget.Outer:
                {
                    return new OuterReadOnlyMemoryPacketParser();
                }
                default:
                {
                    throw new NotImplementedException($"NetworkTarget:{networkTarget.ToString()} Not supported");
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BufferPacketParser CreateBufferPacketParser(NetworkTarget networkTarget)
        {
            switch (networkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    return new InnerBufferPacketParser();
                }
#endif
                case NetworkTarget.Outer:
                {
                    return new OuterBufferPacketParser();
                }
                default:
                {
                    throw new NotImplementedException($"NetworkTarget:{networkTarget.ToString()} Not supported");
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static APacketParser CreateWebglBufferPacketParser(NetworkTarget networkTarget)
        {
            switch (networkTarget)
            {
#if FANTASY_NET
                case NetworkTarget.Inner:
                {
                    return new InnerReadOnlyMemoryPacketParser();
                }
#endif
                case NetworkTarget.Outer:
                {
                    // 判据须与编译进来的 WebSocket 客户端所期望的 parser 类型一致:Unity 端(非 NET/CONSOLE)
                    // 编译的是 WebSocketClientNetworkWebgl,它按 BufferPacketParser 取用;NET/CONSOLE 端的
                    // WebSocketClientNetwork 与服务端 channel 按 ReadOnlyMemoryPacketParser 取用。
                    // 用 FANTASY_WEBGL 作判据比客户端的编译条件窄,在"Unity 端但未定义 FANTASY_WEBGL"(编辑器/
                    // Standalone/Android/iOS)时会返回 ReadOnly 而客户端强转 Buffer,触发 InvalidCastException。
#if !FANTASY_NET && !FANTASY_CONSOLE
                    return new OuterWebglBufferPacketParser();
#else
                    return new OuterReadOnlyMemoryPacketParser();
#endif
                }
                default:
                {
                    throw new NotImplementedException($"NetworkTarget:{networkTarget.ToString()} Not supported");
                }
            }
        }
    }
}