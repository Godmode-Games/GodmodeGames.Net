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
    /// <summary>
    /// Represents an abstract implementation of an UDP socket.
    /// </summary>
    public class RSocket
    {
        public const int DEFAULT_RECEIVER_ROUTE = int.MinValue;


        /// <summary>Queue for outgoing messages.</summary>
        private readonly ConcurrentQueue<RNetMessage> _outgoingMsgQueue
            = new ConcurrentQueue<RNetMessage>();
        /// <summary>Holds information about sent unacknowledged messages. Key is transaction id.</summary>
        private readonly ConcurrentDictionary<int, SentUnacknowledgedMessage> _sentUnacknowledgedMessages
            = new ConcurrentDictionary<int, SentUnacknowledgedMessage>();
        /// <summary>Queue for incoming messages which needs to be dispatched on any thread.</summary>
        private readonly ConcurrentQueue<RNetMessage> _incomingMsgQueue
            = new ConcurrentQueue<RNetMessage>();

        /// <summary>Registered delegates.</summary>
        private IList<ReceiveDelegateDefinition> _receiveDelegates
            = new List<ReceiveDelegateDefinition>();

        private ReceiveDelegate? _defaultReceiverRoute;
        private bool isDefaultRouteRegistered = false;

        private readonly Socket _socket;
        private readonly RSocketSettings _settings;
        private readonly IPacketSerializer _serializer;
        private readonly ILogger? _logger;
        private readonly EndPoint _receiveEndPoint;

        private readonly Task _recvTask;
        private readonly Task _sendTask;

        public RSocket(Socket socket, RSocketSettings settings, IPacketSerializer serializer, IPEndPoint remoteEndPoint, ILogger? logger, CancellationToken cancellationToken)
        {
            _socket = socket;
            _settings = settings;
            _serializer = serializer;
            _logger = logger;
            _receiveEndPoint = remoteEndPoint;

            _recvTask = Task.Factory.StartNew(() => ReceivingTask(cancellationToken), cancellationToken);
            _recvTask.ConfigureAwait(false);
            _sendTask = Task.Factory.StartNew(() => SendingTask(cancellationToken), cancellationToken);
            _sendTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Enqueues message in sending queue, uses own implementation of <see cref="RTransactionGenerator"/> if qosType is reliable.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="data"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="qosType"></param>
        public void Send(int messageId, ref byte[] data, EndPoint remoteEndPoint, RQoSType qosType = RQoSType.Unrealiable)
        {
            var message = (qosType == RQoSType.Realiable) ?
                new RNetMessage(messageId, data, RTransactionGenerator.GenerateId(), remoteEndPoint, qosType) :
                new RNetMessage(messageId, data, null, remoteEndPoint, qosType);

            _outgoingMsgQueue.Enqueue(message);
        }

        /// <summary>
        /// Registers receiver delegate. This function is not threadsafe and should only gets called from dispatcher thread.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="delegate"></param>
        public void RegisterReceiver(int messageId, ReceiveDelegate @delegate)
        {
            if (messageId == DEFAULT_RECEIVER_ROUTE)
            {
                _defaultReceiverRoute = @delegate;
                isDefaultRouteRegistered = true;
                return;
            }

            _receiveDelegates.Add(
                new ReceiveDelegateDefinition(messageId, @delegate)
            );
        }

        /// <summary>
        /// Unregisters receiver.
        /// This function is not threadsafe and should only gets called from dispatcher thread.
        /// </summary>
        /// <param name="messageId"></param>
        public void UnregisterReceiver(int messageId)
        {
            if (messageId == DEFAULT_RECEIVER_ROUTE)
            {
                _defaultReceiverRoute = null;
                isDefaultRouteRegistered = false;
                return;
            }

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
        /// Dispatches incoming message queue into callee thread.
        /// </summary>
        public void Dispatch()
        {
            if (!_incomingMsgQueue.IsEmpty)
            {
                while (_incomingMsgQueue.TryDequeue(out RNetMessage netMsg))
                {
                    // If default route is registered, take that one.
                    // Otherwise search for needed receiver delegate.
                    if (isDefaultRouteRegistered)
                    {
                        _defaultReceiverRoute!.Invoke(netMsg);
                    }
                    else
                    {
                        for (int i = 0; i < _receiveDelegates.Count; ++i)
                        {
                            if (_receiveDelegates[i].MessageId < 0 ||
                               (_receiveDelegates[i].MessageId != null && _receiveDelegates[i].MessageId == netMsg.MessageId))
                            {
                                _receiveDelegates[i].ReceiveDelegate.Invoke(netMsg);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private async Task SendingTask(CancellationToken cancellationToken)
        {
            DateTime startTime;
            while (!cancellationToken.IsCancellationRequested)
            {
                // Store start date time of receiving task to calculate an accurate sending delay.
                startTime = DateTime.Now;

                if (!_outgoingMsgQueue.IsEmpty)
                {
                    if (_outgoingMsgQueue.TryDequeue(out RNetMessage netMsg))
                    {
                        byte[] data = _serializer.Serialize(netMsg);

                        int numOfSentBytes = await _socket.SendToAsync(data, SocketFlags.None, netMsg.RemoteEndPoint);

                        if (numOfSentBytes == 0)
                        {
                            _logger?.WriteWarning(new LogInfo("Sent empty message. MessageId: " + netMsg.MessageId.ToString()));
                        }

                        if (netMsg.QoSType == RQoSType.Realiable)
                        {
                            _sentUnacknowledgedMessages.TryAdd(netMsg.TransactionId!.Value, new SentUnacknowledgedMessage(data, netMsg.RemoteEndPoint, DateTime.Now.AddMinutes(_settings.SendRetryDelay)));
                        }
                    }
                }
                else
                {
                    if (!_sentUnacknowledgedMessages.IsEmpty)
                    {
                        foreach (var unAckMsg in _sentUnacknowledgedMessages)
                        {
                            if (unAckMsg.Value.NextRetryTime < DateTime.Now)
                            {
                                int numOfSentBytes = await _socket.SendToAsync(unAckMsg.Value.SentData, SocketFlags.None, unAckMsg.Value.RemoteEndPoint);

                                if (numOfSentBytes != unAckMsg.Value.SentData.Length)
                                {
                                    // Number of sent bytes unequal to message size.
                                    var errorMsg = "Number of sent bytes unequal to message size. RemoteEndPoint: " + unAckMsg.Value.RemoteEndPoint;
#if DEBUG
                                    throw new Exception(errorMsg);
#elif RELEASE
                                    _logger?.WriteError(new LogInfo(errorMsg));
#endif
                                }

                                if (++unAckMsg.Value.RetriedTimes > _settings.NumberOfSendRetries)
                                {
                                    // Max number of retries reached, remove message from list and log/throw error.
                                    _sentUnacknowledgedMessages.Remove(unAckMsg.Key, out _);

                                    var errorMsg = "Number of max retries reached. RemoteEndPoint: " + unAckMsg.Value.RemoteEndPoint;
#if DEBUG
                                    throw new Exception(errorMsg);
#elif RELEASE
                                    _logger?.WriteError(new LogInfo(errorMsg));
#endif
                                    continue;
                                }
                                    
                                unAckMsg.Value.NextRetryTime = DateTime.Now.AddMilliseconds(_settings.SendRetryDelay);
                            }
                        }
                    }

                    // Nothing to do, take a short break. Delay = SendTickrateInMs - (Now - StartTime)
                    var delay = _settings.SendTickrateInMs - (DateTime.Now.Subtract(startTime).Milliseconds);
                    if (delay > 0)
                    {
                        await Task.Delay(_settings.SendTickrateInMs - (DateTime.Now.Subtract(startTime).Milliseconds));
                    }
                }
            }
        }

        private async Task ReceivingTask(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var data = new ArraySegment<byte>();

                var result = await _socket.ReceiveFromAsync(data, SocketFlags.None, _receiveEndPoint);
                if (result.ReceivedBytes > 0)
                {
                    var dataArray = data.Array;
                    if (_serializer.IsRequest(data.Array))
                    {
                        _incomingMsgQueue.Enqueue(_serializer.Deserialize(dataArray, result.RemoteEndPoint));
                    }
                    else if (_serializer.IsMessageACK(dataArray))
                    {
                        var ackMsg = _serializer.DeserializeACKMessage(dataArray, result.RemoteEndPoint);
                        if (!RemoveSentMessageFromUnacknowledgedMsgQueue(ackMsg))
                        {
                            var errorMsg = "Can't remove non existing network message from unacknowledged message list. MessageId: " + ackMsg.MessageId + " TransactionId: " + ackMsg.TransactionId;
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

        /// <summary>
        /// Removes sent message from unacknowledged message queue.
        /// </summary>
        /// <param name="ackMsg"></param>
        /// <returns></returns>
        private bool RemoveSentMessageFromUnacknowledgedMsgQueue(RReliableNetMessageACK ackMsg)
        {
            return _sentUnacknowledgedMessages.Remove(ackMsg.TransactionId, out SentUnacknowledgedMessage msg);
        }
    }
}
