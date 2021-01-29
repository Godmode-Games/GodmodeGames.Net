using ReforgedNet.LL.Internal;
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
        /// <summary>
        /// Holds information about sent unacknowledged messages.
        /// Key is transaction id.
        /// </summary>
        private ConcurrentDictionary<int, SentUnacknowledgedMessage> _sentUnacknowledgedMessages = new ConcurrentDictionary<int, SentUnacknowledgedMessage>();
        private ConcurrentQueue<RNetMessage> _incomingMsgQueue = new ConcurrentQueue<RNetMessage>();

        private IList<ReceiveDelegateDefinition> _receiveDelegates;

        private readonly Socket _socket;
        private readonly RSocketSettings _settings;
        private readonly IPacketSerializer _serializer;
        private readonly ILogger? _logger;
        private readonly IPEndPoint _EndPoint;

        private Task _recvTask;
        private Task _sendTask;

        public RSocket(Socket socket, RSocketSettings settings, IPacketSerializer serializer, IPEndPoint ep, ILogger? logger, CancellationToken cancellationToken)
        {
            _socket = socket;
            _settings = settings;
            _serializer = serializer;
            _logger = logger;
            _EndPoint = ep;

            _receiveDelegates = new List<ReceiveDelegateDefinition>();

            _recvTask = Task.Factory.StartNew(() => ReceivingTask(cancellationToken), cancellationToken);
            _recvTask.ConfigureAwait(false);
            _sendTask = Task.Factory.StartNew(() => SendingTask(cancellationToken), cancellationToken);
            _sendTask.ConfigureAwait(false);
        }

        public void Send(int messageId, ref byte[] data, EndPoint remoteEndPoint, RQoSType qoSType = RQoSType.Unrealiable)
        {
            _outgoingMsgQueue.Enqueue(new RNetMessage(messageId, data, remoteEndPoint, qoSType));
        }

        public void Send(string messageId, ref byte[] data, EndPoint remoteEndPoint, RQoSType qoSType = RQoSType.Unrealiable)
        {
            _outgoingMsgQueue.Enqueue(new RNetMessage(messageId, data, remoteEndPoint, qoSType));
        }

        /// <summary>
        /// Registers receiver with message id.
        /// This function is not threadsafe and should only gets called from dispatcher thread.
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
        /// This function is not threadsafe and should only gets called from dispatcher thread.
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
        /// Unregisters receiver.
        /// This function is not threadsafe and should only gets called from dispatcher thread.
        /// </summary>
        /// <param name="messageId"></param>
        public void UnregisterReceiver(int messageId)
        {
            int index = 0;
            for (index = 0; index < _receiveDelegates.Count; ++index)
            {
                if (_receiveDelegates[index].MessageId == messageId)
                {
                    break;
                }
            }

            _receiveDelegates.RemoveAt(index);
        }

        /// <summary>
        /// Unregisters receiver.
        /// This function is not threadsafe and should only gets called from dispatcher thread.
        /// </summary>
        /// <param name="method"></param>
        public void UnregisterReceiver(string method)
        {
            int index = 0;
            for (index = 0; index < _receiveDelegates.Count; ++index)
            {
                if (_receiveDelegates[index].Method == method)
                {
                    break;
                }
            }

            _receiveDelegates.RemoveAt(index);
        }


        /// <summary>
        /// Dispatches incoming message queue into callee thread.
        /// </summary>
        public void Dispatch()
        {
            if (!_incomingMsgQueue.IsEmpty)
            {
                RNetMessage netMsg = null;
                while (_incomingMsgQueue.TryDequeue(out netMsg))
                {
                    for (int i = 0; i < _receiveDelegates.Count; ++i)
                    {
                        if (_receiveDelegates[i].MessageId < 0 ||
                            (_receiveDelegates[i].MessageId != null && _receiveDelegates[i].MessageId == netMsg.MessageId)
                            || (_receiveDelegates[i].Method != null && _receiveDelegates[i].Method == netMsg.Method))
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
                            _sentUnacknowledgedMessages.TryAdd(netMsg.TransactionId!.Value, new SentUnacknowledgedMessage(data, netMsg.RemoteEndPoint));
                        }
                    }
                }
                else
                {
                    // Procceed unacknowledged reliable message inside the queue.
                    if (!_sentUnacknowledgedMessages.IsEmpty)
                    {
                        foreach (var unAckMsg in _sentUnacknowledgedMessages)
                        {
                            if (unAckMsg.Value.NextRetryTime < DateTime.Now)
                            {
                                int numOfSentBytes = await _socket.SendToAsync(unAckMsg.Value.SentData, SocketFlags.None, unAckMsg.Value.RemoteEndPoint);

                                if (numOfSentBytes != unAckMsg.Value.SentData.Length)
                                {
                                    // Write log
                                }

                                ++unAckMsg.Value.RetriedTimes;
                                unAckMsg.Value.NextRetryTime = DateTime.Now.AddMilliseconds(SENT_RELIABLE_MESSAGE_RETRY_DELAY);
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
            //args.SetBuffer(new Memory<byte>());
            byte[] asyncBuffer = new byte[8 * 1024];
            args.SetBuffer(asyncBuffer, 0, asyncBuffer.Length);
            args.RemoteEndPoint = _EndPoint;

            args.Completed += new EventHandler<SocketAsyncEventArgs>(this.ReceivedData);

            // Start receiving loop.
            if (!_socket.ReceiveFromAsync(args))
            {
                this.ReceivedData(_socket, args);
            }
        }

        /// <summary>
        /// Removes sent message from unacknowledged message queue.
        /// </summary>
        /// <param name="ackMsg"></param>
        /// <returns></returns>
        private bool RemoveSentMessageFromUnacknowledgedMsgQueue(RReliableNetMessageACK ackMsg)
        {
            return _sentUnacknowledgedMessages.Remove(ackMsg.TransactionId, out SentUnacknowledgedMessage msg);
        }

        /// <summary>
        /// Receive data async
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReceivedData(object sender, SocketAsyncEventArgs e)
        {
            // Load information and start listening again.
            int numOfRecvBytes = e.BytesTransferred;
            //byte[] data = e.MemoryBuffer.ToArray();
            byte[] data = new byte[numOfRecvBytes];
            Array.Copy(e.Buffer, e.Offset, data, 0, numOfRecvBytes);
            var ep = e.RemoteEndPoint;

            if (!_socket.ReceiveFromAsync(e))
            {
                this.ReceivedData(sender, e);
            }

            if (numOfRecvBytes > 0)
            {
                if (_serializer.IsRequest(data))
                {
                    _incomingMsgQueue.Enqueue(_serializer.Deserialize(data));
                }
                else if (_serializer.IsMessageACK(data))
                {
                    var ackMsg = _serializer.DeserializeACKMessage(data);
                    if (!RemoveSentMessageFromUnacknowledgedMsgQueue(ackMsg))
                    {
                        var errorMsg = "Can't remove non existing network message from unacknowledged message list.";
                        if (ackMsg.MessageId != null)
                        {
                            errorMsg += " MessageId: " + ackMsg.MessageId;
                        }
                        else if (ackMsg.Method != null)
                        {
                            errorMsg += " Method: " + ackMsg.Method;
                        }
                        errorMsg += " TransactionId: " + ackMsg.TransactionId;
#if DEBUG
                        throw new Exception(errorMsg);
#elif RELEASE
                                _logger?.WriteError(new LogInfo()
                                {
                                    OccuredDateTime = DateTime.Now,
                                    Message = errorMsg
                                });
#endif
                    }
                }
            }
        }
    }
}
