using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.LL
{
    public class RSocket
    {
        public const int SENDING_IDLE_DELAY = 1000 / 50;
        public const int SENT_RELIABLE_MESSAGE_RETRY_DELAY = 1000 / 10;

        private ConcurrentQueue<RNetMessage> _outgoingMsgQueue = new ConcurrentQueue<RNetMessage>();
        private ConcurrentBag<(RNetMessage, DateTime)> _sentUnansweredMsgQueue = new ConcurrentBag<(RNetMessage, DateTime)>();
        private ConcurrentQueue<RNetMessage> _incomingMsgQueue = new ConcurrentQueue<RNetMessage>();

        private IList<ReceiveDelegateDefinition> _receiveDelegates;

        private readonly Socket _socket;
        private readonly IPacketSerializer _serializer;

        private Task _recvTask;
        private Task _sendTask;

        public RSocket(Socket socket, IPacketSerializer serializer, CancellationToken cancellationToken)
        {
            _socket = socket;
            _serializer = serializer;

            _receiveDelegates = new List<ReceiveDelegateDefinition>();

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

        /// <summary>
        /// Registers receiver with message id.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="delegate"></param>
        public void RegisterReceiver(int messageId, ReceiveDelegate @delegate)
        {
            _receiveDelegates.Add(
                new ReceiveDelegateDefinition(messageId, @delegate)
            );
        }

        /// <summary>
        /// Registers receiver with method name.
        /// </summary>
        /// <param name="method"></param>
        /// <param name="delegate"></param>
        public void RegisterReceiver(string method, ReceiveDelegate @delegate)
        {
            _receiveDelegates.Add(
                new ReceiveDelegateDefinition(method, @delegate)
            );
        }

        /// <summary>
        /// Dispatches incoming message queue into callee thread.
        /// </summary>
        public void Dispatch()
        {
            if (!_incomingMsgQueue.IsEmpty)
            {
                foreach (var netMsg in _incomingMsgQueue)
                {
                    for (int i = 0; i < _receiveDelegates.Count; ++i)
                    {
                        if (_receiveDelegates[i].MessageId != null && _receiveDelegates[i].MessageId == netMsg.MessageId
                            || _receiveDelegates[i].Method != null && _receiveDelegates[i].Method == netMsg.Method)
                        {
                            _receiveDelegates[i].ReceiveDelegate.Invoke(netMsg);
                            break;
                        }
                    }
                }
            }
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

                        if (netMsg.QoSType == RQoSType.Realiable)
                        {
                            _sentUnansweredMsgQueue.Add((netMsg, DateTime.Now.AddMilliseconds(SENT_RELIABLE_MESSAGE_RETRY_DELAY)));
                        }
                    }
                }
                else
                {
                    // Procceed unanswered reliable message inside the queue.
                    if (!_sentUnansweredMsgQueue.IsEmpty)
                    {
                        foreach (var netMsg in _sentUnansweredMsgQueue)
                        {
                            if (netMsg.Item2 > DateTime.Now)
                            {
                                byte[] data = _serializer.Serialize(netMsg.Item1);
                                int numOfSentBytes = await _socket.SendToAsync(data, SocketFlags.None, netMsg.Item1.RemoteEndPoint);
                            }
                        }
                    }

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
#if DEBUG
                    throw new IndexOutOfRangeException($"Received message with a length of 0 or buffer length unequal num of received bytes. NumberOfReceivedBytes {numOfRecvBytes} - Buffer length: {e.Buffer.Length}");
#elif RELEASE
                    // Log message
#endif
                }

            };
            
            // Start receiving loop.
            _socket.ReceiveFromAsync(args);
        }
    }
}
