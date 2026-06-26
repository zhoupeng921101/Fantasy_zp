#if FANTASY_UNITY || FANTASY_CONSOLE
using System;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network.Interface;
using UnityEngine;

namespace Fantasy.Network
{
    internal static class IRequestSupportedChecker<T> where T : IMessage
    {
        public static bool IsRequest { get; }
        static IRequestSupportedChecker()
        {
            IsRequest= typeof(IRequest).IsAssignableFrom(typeof(T));
        }
    }
    
    public class DebugClientSession : Session
    {
        [HideInCallstack]
        public override void Send<T>(T message, uint rpcId = 0, long address = 0)
        {
            if(!IRequestSupportedChecker<T>.IsRequest)
            {
                Log.Debug($"Send Message: {typeof(T).Name} Json:{message.ToJsonIndented()}");
            }
            
            base.Send(message, rpcId, address);
        }
        [HideInCallstack]
        public override void Send(IMessage message, Type messageType, uint rpcId = 0, long address = 0)
        {
            if(message is not IRequest)
            {
                Log.Debug($"Send Message: {messageType.Name} Json:{message.ToJsonIndented()}");
            }
            
            base.Send(message, messageType, rpcId, address);
        }
        [HideInCallstack]
        public override FTask<IResponse> Call<T>(T request, long address = 0)
        {
            if (request.OpCode() != 4026531841)
            {
                Log.Debug($"Send Message: {typeof(T).Name} Json:{request.ToJsonIndented()}");
            }
        
            return base.Call(request, address);
        }
    }
}
#endif