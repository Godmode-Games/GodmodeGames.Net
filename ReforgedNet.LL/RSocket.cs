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

        /// <summary>Gets invoked if an internal error occurs.</summary>
        public Action? Error;

        /// <summary>Queue for outgoing messages.</summary>
        protected readonly ConcurrentQueue<RNetMessage> _outgoingMsgQueue
            = new ConcurrentQueue<RNetMessage>();
        /// <summary>Holds information about sent unacknowledged messages. Key is transaction id.</summary>
        protected readonly ConcurrentDictionary<int, SentUnacknowledgedMessage> _sentUnacknowledgedMessages
            = new ConcurrentDictionary<int, SentUnacknowledgedMessage>();
        /// <summary>Queue for incoming messages which needs to be dispatched on any thread.</summary>
        protected readonly ConcurrentQueue<RNetMessage> _incomingMsgQueue
            = new ConcurrentQueue<RNetMessage>();
        protected readonly ConcurrentQueue<RReliableNetMessageACK> _pendingACKMessages = new ConcurrentQueue<RReliableNetMessageACK>();

        /// <summary>Registered delegates.</summary>
        protected IList<ReceiveDelegateDefinition> _receiveDelegates
            = new List<ReceiveDelegateDefinition>();

        protected ReceiveDelegate? _defaultReceiverRoute;
        protected bool isDefaultRouteRegistered = false;

        protected Socket? _socket;
        protected readonly RSocketSettings _settings;
        protected readonly IPacketSerializer _serializer;
        protected readonly ILogger? _logger;
        public EndPoint RemoteEndPoint;

        protected Task? _recvTask = null;
        protected Task? _sendTask = null;
        protected CancellationTokenSource _cts;

        #region Statistics
        /// <summary>
        /// Total amount of sent bytes
        /// </summary>
        public long SendBytesTotal = 0;
        /// <summary>
        /// Total amount of received bytes
        /// </summary>
        public long ReceivedBytesTotal = 0;
        /// <summary>
        /// Amount of bytes sent in last second
        /// </summary>
        public long SendLastSecond = 0;
        protected long SendCurrentSecond = 0;
        protected int CurrentSecondSend = 0;
        /// <summary>
        /// Amount of bytes received last second
        /// </summary>
        public long ReceiveLastSecond = 0;
        protected long ReceiveCurrentSecond = 0;
        protected int CurrentSecondReceive = 0;
        /// <summary>
        /// Datetime of last received data
        /// </summary>
        public DateTime? LastDataReceived = null;
        #endregion

        public RSocket(RSocketSettings settings, IPEndPoint remoteEndPoint, IPacketSerializer serializer, ILogger? logger)
        {
            _settings = settings;
            _serializer = serializer;
            _logger = logger;
            RemoteEndPoint = remoteEndPoint;

            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// Enqueues message in sending queue, uses own implementation of <see cref="RTransactionGenerator"/> if qosType is reliable.
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="data"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="qosType"></param>
        public void Send(int messageId, ref byte[] data, EndPoint remoteEndPoint, RQoSType qosType = RQoSType.Unrealiable, Action? failCallback = null)
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
        public void RegisterReceiver(int? messageId, ReceiveDelegate @delegate)
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
                    if (netMsg.MessageId != null && isDefaultRouteRegistered)
                    {
                        _defaultReceiverRoute!.Invoke(netMsg);
                    }
                    else
                    {
                        for (int i = 0; i < _receiveDelegates.Count; ++i)
                        {
                            if (_receiveDelegates[i].MessageId < 0 || _receiveDelegates[i].MessageId == netMsg.MessageId)
                            {
                                _receiveDelegates[i].ReceiveDelegate.Invoke(netMsg);
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Closes socket and releases ressources.
        /// </summary>
        public void Close()
        {
            _cts.Cancel();
            _socket?.Close();
        }

        protected async Task SendingTask(CancellationToken cancellationToken)
        {
            DateTime startTime;
            while (!cancellationToken.IsCancellationRequested)
            {
                // Store start date time of receiving task to calculate an accurate sending delay.
                startTime = DateTime.Now;

                if (_socket != null && !_outgoingMsgQueue.IsEmpty)
                {
                    while (_outgoingMsgQueue.TryDequeue(out RNetMessage netMsg))
                    {
                        byte[] data = _serializer.Serialize(netMsg);
                        int numOfSentBytes = _socket.SendTo(data, 0, data.Length, SocketFlags.None, netMsg.RemoteEndPoint);

                        //int numOfSentBytes = await _socket.SendToAsync(data, SocketFlags.None, netMsg.RemoteEndPoint);

                        if (numOfSentBytes == 0)
                        {
                            _logger?.WriteWarning(new LogInfo("Sent empty message. MessageId: " + netMsg.MessageId?.ToString()));
                        }
                        else
                        {
                            //Update statistics...
                            UpdateSendStatistics(numOfSentBytes);

                            //Start receiving...
                            this.StartReceiverTask();
                        }

                        if (netMsg.QoSType == RQoSType.Realiable)
                        {
                            _sentUnacknowledgedMessages.TryAdd(netMsg.TransactionId!.Value, new SentUnacknowledgedMessage(data, netMsg.RemoteEndPoint, DateTime.Now.AddMilliseconds(_settings.SendRetryDelay)));
                        }
                    }
                }

                if (_socket != null && !_sentUnacknowledgedMessages.IsEmpty)
                {
                    foreach (var unAckMsg in _sentUnacknowledgedMessages)
                    {
                        if (unAckMsg.Value.NextRetryTime < DateTime.Now)
                        {
                            int numOfSentBytes = _socket.SendTo(unAckMsg.Value.SentData, 0, unAckMsg.Value.SentData.Length, SocketFlags.None, unAckMsg.Value.RemoteEndPoint);

                            if (numOfSentBytes != unAckMsg.Value.SentData.Length)
                            {
                                // Number of sent bytes unequal to message size.
                                var errorMsg = "Number of sent bytes unequal to message size. RemoteEndPoint: " + unAckMsg.Value.RemoteEndPoint;
                                _logger?.WriteError(new LogInfo(errorMsg));
                                Error?.Invoke();
                            }

                            UpdateSendStatistics(numOfSentBytes);

                            if (++unAckMsg.Value.RetriedTimes > _settings.NumberOfSendRetries)
                            {
                                _sentUnacknowledgedMessages.TryRemove(unAckMsg.Key, out _);

                                var errorMsg = "Number of max retries reached. RemoteEndPoint: " + unAckMsg.Value.RemoteEndPoint;
                                _logger?.WriteError(new LogInfo(errorMsg));
                                Error?.Invoke();
                                continue;
                            }
                                    
                            unAckMsg.Value.NextRetryTime = DateTime.Now.AddMilliseconds(_settings.SendRetryDelay);
                        }
                    }
                }

                if (_socket != null && !_pendingACKMessages.IsEmpty)
                {
                    while (_pendingACKMessages.TryDequeue(out RReliableNetMessageACK ackMsg))
                    {
                        //byte[] data = _serializer.SerializeACKMessage(ackMsg);

                        byte[] data = _serializer.SerializeACKMessage(ackMsg);
                        int numOfSentBytes = _socket.SendTo(data, 0, data.Length, SocketFlags.None, ackMsg.RemoteEndPoint);

                        if (numOfSentBytes == 0)
                        {
                            _logger?.WriteWarning(new LogInfo("Sent empty ack message. MessageId: " + ackMsg.MessageId?.ToString()));
                        }
                        else
                        {
                            //Update statistics...
                            UpdateSendStatistics(numOfSentBytes);
                        }
                    }
                }
                    
                // Nothing to do, take a short break. Delay = SendTickrateInMs - (Now - StartTime)
                var delay = _settings.SendTickrateIsMs - (DateTime.Now.Subtract(startTime).TotalMilliseconds);
                if (delay > 0)
                {
                    await Task.Delay((int)delay);
                }
            }
        }

        protected void ReceivingTask(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var data = new byte[4096];

                var numOfReceivedBytes = _socket!.ReceiveFrom(data, 0, 4096, SocketFlags.None, ref RemoteEndPoint);

                if (numOfReceivedBytes > 0)
                {
                    UpdateReceiveStatistics(numOfReceivedBytes);
                    if (_serializer.IsRequest(data))
                    {
                        RNetMessage? msg = _serializer.Deserialize(data, RemoteEndPoint);
                        if (msg == null)
                        {
#if DEBUG
                            throw new Exception("Serializing of RNetMessage failed!");
#elif RELEASE
                            _logger?.WriteError(new LogInfo("Serializing of RNetMessage failed!"));
#endif
                        }
                        else
                        {
                            _incomingMsgQueue.Enqueue(msg);
                            if (msg.QoSType == RQoSType.Realiable)
                            {
#pragma warning disable CS8629 // Nullable value type may be null.
                                _pendingACKMessages.Enqueue(new RReliableNetMessageACK(msg.MessageId, (int)msg.TransactionId, msg.RemoteEndPoint));
#pragma warning restore CS8629 // Nullable value type may be null.
                            }
                        }
                    }
                    else if (_serializer.IsMessageACK(data))
                    {
                        var ackMsg = _serializer.DeserializeACKMessage(data, RemoteEndPoint);
                        if (ackMsg == null)
                        {
#if DEBUG
                            throw new Exception("Serializing of ACKRNetMessage failed!");
#elif RELEASE
                            _logger?.WriteError(new LogInfo("Serializing of ACKRNetMessage failed!"));
#endif
                        }
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
        protected bool RemoveSentMessageFromUnacknowledgedMsgQueue(RReliableNetMessageACK ackMsg)
        {
            return _sentUnacknowledgedMessages.TryRemove(ackMsg.TransactionId, out SentUnacknowledgedMessage msg);
        }

        /// <summary>
        /// Creates Socket-Object
        /// </summary>
        protected void CreateSocket()
        {
            _socket = new Socket(this.RemoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        }

        /// <summary>
        /// Starts Receiver-Task
        /// </summary>
        protected void StartReceiverTask()
        {
            if (_socket == null)
            {
                return;
            }
            if (_recvTask == null)
            {
                _recvTask = Task.Factory.StartNew(() => ReceivingTask(_cts.Token), _cts.Token);
                _recvTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Update Sent-Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        private void UpdateSendStatistics(int numOfSentBytes)
        {
            this.SendBytesTotal += numOfSentBytes;

            int curr = DateTime.Now.Second;
            if (curr == this.CurrentSecondReceive)
            {
                this.ReceiveCurrentSecond += numOfSentBytes;
            }
            else
            {
                this.ReceiveLastSecond = this.ReceiveCurrentSecond;
                this.ReceiveCurrentSecond = numOfSentBytes;
                this.CurrentSecondReceive = curr;
            }
        }

        /// <summary>
        /// Update Receive Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        private void UpdateReceiveStatistics(int numOfReceivedBytes)
        {
            //Statisktiken
            this.LastDataReceived = DateTime.Now;
            this.ReceivedBytesTotal += numOfReceivedBytes;

            int curr = DateTime.Now.Second;
            if (curr == this.CurrentSecondReceive)
            {
                this.ReceiveCurrentSecond += numOfReceivedBytes;
            }
            else
            {
                this.ReceiveLastSecond = this.ReceiveCurrentSecond;
                this.ReceiveCurrentSecond = numOfReceivedBytes;
                this.CurrentSecondReceive = curr;
            }
        }
    }
}
