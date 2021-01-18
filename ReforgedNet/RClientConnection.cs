using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
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

        public int Invoke(RBasePacket packet)
        {
            return Send(_socket, packet).Result;
        }

        public async Task<int> InvokeAsync(RBasePacket packet, CancellationToken cancellationToken)
        {
            return await Send(_socket, packet);
        }

        public int Invoke(RBasePacket packet, params ResponseDelegate[] callbacks)
        {
            foreach (var callback in callbacks)
            {
                RegisterResponse(callback);
            }

            return Send(_socket, packet, new CancellationToken()).Result;
        }

        public void RegisterResponse(ResponseDelegate callback)
        {
            _registeredResponseCallbacks.Add(callback);
        }

        public void Dispatch()
        {

        }
    }
}
