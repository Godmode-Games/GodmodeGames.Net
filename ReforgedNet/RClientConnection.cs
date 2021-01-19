using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet
{
    public class RClientConnection : RSocket
    {
        public delegate void ResponseDelegate(RBasePacket message);

        private RClientSettings _settings;
        private Socket _socket;

        private ConcurrentBag<ResponseDelegate> _registeredResponseCallbacks;

        public RClientConnection(RClientSettings settings)
        {
            _settings = settings;
        }

        public int Invoke(RBasePacket packet, RSendingChannel channel = RSendingChannel.Realiable)
        {
            return Send(_socket, packet).Result;
        }

        public async Task<int> InvokeAsync(RBasePacket packet, CancellationToken cancellationToken, RSendingChannel channel = RSendingChannel.Realiable)
        {
            return await Send(_socket, packet);
        }

        public int Invoke(RBasePacket packet, RSendingChannel chanell = RSendingChannel.Realiable, params ResponseDelegate[] callbacks)
        {
            foreach (var callback in callbacks)
            {
                RegisterResponseReceiver(callback);
            }

            return Send(_socket, packet, new CancellationToken()).Result;
        }

        public void RegisterResponseReceiver(ResponseDelegate callback)
        {
            _registeredResponseCallbacks.Add(callback);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegisterGenericResponseReceiver<T>(GenericResponseDelegate<T> callback)
            where T : RBasePacket
        {
            RegisterResponseReceiver((RBasePacket basePacket) =>
            {
                callback.Invoke((T)basePacket);
            });
        }


        public int Dispatch()
        {
            return 0;
        }

        public int Dispatch(RSendingChannel channel)
        {
            if (channel == default)
            {
                // return number of dispatched delegates.
                
            }
            return 0;
        }
    }
}
