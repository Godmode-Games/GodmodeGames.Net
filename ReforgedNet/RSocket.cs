using ReforgedNet.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet
{
    public class RNetworkMsg
    {
        public readonly byte[] Data;
    }

    public abstract class RSocket
    {
        protected Queue<RNetworkMsg> _outgoingMsgQueue;
        protected Queue<RNetworkMsg> _incomingMsgQueue;

        protected ConcurrentQueue<RNetworkMsg> _outgoingThreadsafeMsgQueue;
        protected ConcurrentQueue<RNetworkMsg> _incomingThreadsafeMsgQueue;

        private RPacketSerializer _serializer;

        public RSocket()
        {
            // Start receiver thread.
            //Thread t = new Thread(new ParameterizedThreadStart(new Socket()))
            //t.Start();
        }

        protected async Task<int> Send(Socket socket, RBasePacket packet, CancellationToken cancellationToken = default)
        {
            return await socket.SendAsync(_serializer.Serialize(packet), SocketFlags.None, cancellationToken);
        }

        protected async Task<(int, Memory<byte>)> Receive(Socket socket, CancellationToken cancellationToken = default)
        {
            Memory<byte> buffer = new Memory<byte>();
            int numberOfBytes = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

            return (numberOfBytes, buffer);
        }
    }
}
