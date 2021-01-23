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
        public const int SENDING_IDLE_DELAY = 1000 / 50;

        private ConcurrentQueue<RNetMessage> _outgoingMsgQueue;
        private ConcurrentQueue<RNetMessage> _incomingMsgQueue;

        private ICollection<ReceiveDelegate> _receiveDelegates;

        private IPacketSerializer _serializer;

        private Socket _socket;

        private Task _recvTask;
        private Task _sendTask;

        public RSocket(CancellationToken cancellationToken)
        {
            _receiveDelegates = new List<ReceiveDelegate>();

            _recvTask = Task.Factory.StartNew(() => ReceivingTask(cancellationToken), cancellationToken);
            _recvTask.ConfigureAwait(false);
            _sendTask = Task.Factory.StartNew(() => SendingTask(cancellationToken), cancellationToken);
            _sendTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Adds network message to outgoing message queue.
        /// </summary>
        /// <param name="message"></param>
        public void Send(RNetMessage message)
            => _outgoingMsgQueue.Enqueue(message); // TODO: Add validation?

        public void RegisterReceiver(ReceiveDelegate @delegate)
            => _receiveDelegates.Add(@delegate);

        public void Dispatch()
        {

        }

        private async Task SendingTask(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_outgoingMsgQueue.IsEmpty)
                {
                    if (_outgoingMsgQueue.TryDequeue(out RNetMessage netMsg))
                    {
                        byte[] data = _serializer.Serialize(netMsg);

                        int numOfSentBytes = await _socket.SendToAsync(data, SocketFlags.None, netMsg.RemoteEndPoint);
                    }
                }
                else
                {
                    // Nothing to do, take a short break.
                    await Task.Delay(SENDING_IDLE_DELAY);
                }
            }
        }

        private void ReceivingTask(CancellationToken cancellationToken)
        {
            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.SetBuffer(new Memory<byte>());

            args.Completed += (object sender, SocketAsyncEventArgs e) =>
            {
                // Load information and start listening again.
                int numOfRecvBytes = e.BytesTransferred;
                byte[] data = e.MemoryBuffer.ToArray();
                var ep = e.RemoteEndPoint;

                // Start receiving again, if cancellation is not requested.
                if (!cancellationToken.IsCancellationRequested)
                {
                    _socket.ReceiveFromAsync(args);
                }

                if (numOfRecvBytes > 0 && numOfRecvBytes == data.Length)
                {
                    _incomingMsgQueue.Enqueue(_serializer.Deserialize(data));
                }
                else
                {
                    // Log Exception
                }

            };
            
            // Start receiving loop.
            _socket.ReceiveFromAsync(args);
        }
    }
}
