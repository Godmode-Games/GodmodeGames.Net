using ReforgedNet.LL.Internal;
using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.LL
{
    /// <summary>
    /// Represents an abstract implementation of an UDP socket.
    /// </summary>
    public class RSocket
    {
        /// <summary>Gets invoked if an internal error occurs.</summary>
        public Action<long>? Error;

        public Action<byte[], IPEndPoint>? OnReceiveData = null;
        protected Action<RNetMessage>? OnReceiveInternalData = null;

        /// <summary>Queue for outgoing messages.</summary>
        protected readonly ConcurrentQueue<RNetMessage> _outgoingMsgQueue
            = new ConcurrentQueue<RNetMessage>();
        /// <summary>Holds information about sent unacknowledged messages. Key is transaction id.</summary>
        protected readonly ConcurrentDictionary<long, SentUnacknowledgedMessage> _sentUnacknowledgedMessages
            = new ConcurrentDictionary<long, SentUnacknowledgedMessage>();
        /// <summary>Queue for incoming messages which needs to be dispatched on any thread.</summary>
        protected readonly ConcurrentQueue<RNetMessage> _incomingMsgQueue
            = new ConcurrentQueue<RNetMessage>();
        protected readonly ConcurrentQueue<RReliableNetMessageACK> _pendingACKMessages = new ConcurrentQueue<RReliableNetMessageACK>();

        protected Socket? _socket;
        protected readonly RSocketSettings _settings;
        protected readonly IPacketSerializer _serializer;
        protected readonly ILogger? _logger;
        public EndPoint RemoteEndPoint;

        protected Task? _recvTask = null;
        protected Task? _sendTask = null;
        protected CancellationTokenSource _cts;

        protected List<long> _lastMessagesReceived = new List<long>();

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
        public DateTime LastDataReceived = DateTime.Now;

        public long TotalPacketsSent = 0;
        public long TotalPacketsLost = 0;
        public long TotalPacketsIncomplete = 0;
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
        public void Send(ref byte[] data, IPEndPoint remoteEndPoint, RQoSType qosType = RQoSType.Unrealiable)
        {
            if (!_cts.Token.IsCancellationRequested)
            {
                RNetMessage message;
                switch (qosType)
                {
                    default:
                    case RQoSType.Realiable:
                    case RQoSType.Internal:
                        message = new RNetMessage(data, RTransactionGenerator.GenerateId(), remoteEndPoint, qosType);
                        break;
                    case RQoSType.Unrealiable:
                        message = new RNetMessage(data, null, remoteEndPoint, qosType);
                        break;
                }
                _outgoingMsgQueue.Enqueue(message);
            }
        }

        /// <summary>
        /// Dispatches incoming message queue into callee thread.
        /// </summary>
        public virtual void Dispatch()
        {
            if (!_incomingMsgQueue.IsEmpty)
            {
                while (_incomingMsgQueue.TryDequeue(out RNetMessage netMsg))
                {
                    // check, if the message was already received and dispatched.
                    // maybe the sender had bad ping and resend message for reliability
                    HandleIncommingMessages(netMsg);
                }
            }
        }

        /// <summary>
        /// Closes socket and releases ressources.
        /// </summary>
        public void Close()
        {
            _cts.Cancel();
            _sendTask?.Wait();
            _socket?.Close();
        }

        protected void HandleIncommingMessages(RNetMessage netMsg)
        {
            if (netMsg.QoSType != RQoSType.Unrealiable && netMsg.TransactionId != null)
            {
                if (_lastMessagesReceived.Contains(netMsg.TransactionId.Value))
                {
                    _logger?.WriteInfo(new LogInfo("skipping already received message."));
                    return;
                }
                _lastMessagesReceived.Add(netMsg.TransactionId.Value);
                if (_lastMessagesReceived.Count > _settings.StoreLastMessages)
                {
                    _lastMessagesReceived.RemoveAt(0);
                }
            }

            if (netMsg.QoSType == RQoSType.Internal)
            {
                OnReceiveInternalData?.Invoke(netMsg);
            }
            else
            {
                OnReceiveData?.Invoke(netMsg.Data, (IPEndPoint)netMsg.RemoteEndPoint);
            }
        }

        protected async Task SendingTask(CancellationToken cancellationToken)
        {
            DateTime startTime;
            while (!_outgoingMsgQueue.IsEmpty || !cancellationToken.IsCancellationRequested)
            {
                // Store start date time of receiving task to calculate an accurate sending delay.
                startTime = DateTime.Now;

                //Send Messages...
                if (_socket != null && !_outgoingMsgQueue.IsEmpty)
                {
                    while (_outgoingMsgQueue.TryDequeue(out RNetMessage netMsg))
                    {
                        byte[] data;
                        try
                        {
                            data = _serializer.Serialize(netMsg);
                        }
                        catch(Exception ex)
                        {
                            _logger?.WriteError(new LogInfo("error while serializing netmessage: " + ex.Message));
                            continue;
                        }

                        int numOfSentBytes = _socket.SendTo(data, 0, data.Length, SocketFlags.None, netMsg.RemoteEndPoint);

                        if (numOfSentBytes == 0)
                        {
                            _logger?.WriteWarning(new LogInfo("Sent empty message. TransactionId: " + netMsg.TransactionId?.ToString()));
                        }
                        else
                        {
                            //Update statistics...
                            UpdateSendStatistics(numOfSentBytes);

                            //Start receiving...
                            StartReceiverTask();
                        }

                        if (netMsg.QoSType == RQoSType.Realiable || netMsg.QoSType == RQoSType.Internal)
                        {
                            _sentUnacknowledgedMessages.TryAdd(netMsg.TransactionId!.Value, new SentUnacknowledgedMessage(data, netMsg.RemoteEndPoint, DateTime.Now.AddMilliseconds(_settings.SendRetryDelay), netMsg.TransactionId.Value));
                        }
                    }
                }

                //resend unacknowledged Messages 
                if (_socket != null && !_sentUnacknowledgedMessages.IsEmpty && !cancellationToken.IsCancellationRequested)
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
                                Error?.Invoke(unAckMsg.Value.TransactionId);
                            }
                            else if (numOfSentBytes > 0)
                            {
                                //Start receiving...
                                StartReceiverTask();
                            }

                            UpdateSendStatistics(numOfSentBytes);

                            if (++unAckMsg.Value.RetriedTimes > _settings.NumberOfSendRetries)
                            {
                                _sentUnacknowledgedMessages.TryRemove(unAckMsg.Key, out _);

                                var errorMsg = "Number of max retries reached. RemoteEndPoint: " + unAckMsg.Value.RemoteEndPoint;

                                TotalPacketsLost++;

                                _logger?.WriteError(new LogInfo(errorMsg));
                                Error?.Invoke(unAckMsg.Value.TransactionId);
                                continue;
                            }
                                    
                            unAckMsg.Value.NextRetryTime = DateTime.Now.AddMilliseconds(_settings.SendRetryDelay);
                        }
                    }
                }

                //Send ACK Messages
                if (_socket != null && !_pendingACKMessages.IsEmpty)
                {
                    while (_pendingACKMessages.TryDequeue(out RReliableNetMessageACK ackMsg))
                    {
                        byte[] data;
                        try
                        {
                            data = _serializer.SerializeACKMessage(ackMsg);
                        }
                        catch (Exception ex)
                        {
                            _logger?.WriteError(new LogInfo("error while serializing ack message: " + ex.Message));
                            continue;
                        }
                        int numOfSentBytes = _socket.SendTo(data, 0, data.Length, SocketFlags.None, ackMsg.RemoteEndPoint);

                        if (numOfSentBytes == 0)
                        {
                            _logger?.WriteWarning(new LogInfo("Sent empty ack message. TransactionId: " + ackMsg.TransactionId.ToString()));
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
                if (!cancellationToken.IsCancellationRequested && delay > 0)
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

                int numOfReceivedBytes = 0;
                try
                {
                    numOfReceivedBytes = _socket!.ReceiveFrom(data, 0, 4096, SocketFlags.None, ref RemoteEndPoint);
                }
                catch (Exception ex)
                {
                    _logger?.WriteError(new LogInfo("error while receiving data: " + ex.Message));
                    continue;
                }

                if (numOfReceivedBytes > 0)
                {
                    UpdateReceiveStatistics(numOfReceivedBytes);
                    if (_serializer.IsRequest(data))
                    {
                        RNetMessage? msg = null;
                        EDeserializeError deserialize_error = EDeserializeError.None;
                        try
                        {
                            msg = _serializer.Deserialize(data, (IPEndPoint)RemoteEndPoint, out deserialize_error);
                        }
                        catch(Exception ex)
                        {
                            _logger?.WriteError(new LogInfo("Serializing of RNetMessage failed: " + ex.Message));
                            continue;
                        }

                        if (deserialize_error != EDeserializeError.None || msg == null)
                        {
                            _logger?.WriteError(new LogInfo("Got incomplete RNetMessage!"));
                            TotalPacketsIncomplete++;
                            continue;
                        }
                        else if (msg.QoSType == RQoSType.Internal)
                        {
                            //Handle internal Messages instant
                            _pendingACKMessages.Enqueue(new RReliableNetMessageACK(msg.TransactionId!.Value, msg.RemoteEndPoint));
                            HandleIncommingMessages(msg);
                        }
                        else 
                        {
                            if (msg.QoSType == RQoSType.Realiable || msg.QoSType == RQoSType.Internal)
                            {
                                //Send ACK Message
                                _pendingACKMessages.Enqueue(new RReliableNetMessageACK(msg.TransactionId!.Value, msg.RemoteEndPoint));
                            }

                            if (_settings.HandleMessagesInMainThread == true)
                            {
                                _incomingMsgQueue.Enqueue(msg);
                            }
                            else
                            {
                                HandleIncommingMessages(msg);
                            }
                        }
                    }
                    else if (_serializer.IsMessageACK(data))
                    {
                        RReliableNetMessageACK? ackMsg = null;
                        try
                        {
                            ackMsg = _serializer.DeserializeACKMessage(data, RemoteEndPoint);
                        }
                        catch(Exception ex)
                        {
                            _logger?.WriteError(new LogInfo("Serializing of ACKRNetMessage failed: " + ex.Message));
                            continue;
                        }
                        if (ackMsg != null)
                        {
                            if (!RemoveSentMessageFromUnacknowledgedMsgQueue(ackMsg))
                            {
                                var errorMsg = "Can't remove non existing network message from unacknowledged message list. TransactionId: " + ackMsg.TransactionId;
                                _logger?.WriteError(new LogInfo(errorMsg));
                            }
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
            _socket = new Socket(RemoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            _socket.DontFragment = true;
        }

        /// <summary>
        /// Starts receiver-task
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
        /// Starts sending-task
        /// </summary>
        protected void StartSendingTask()
        {
            if (_socket == null)
            {
                return;
            }

            if (_sendTask == null)
            {
                _sendTask = Task.Factory.StartNew(() => SendingTask(_cts.Token), _cts.Token);
                _sendTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Update Sent-Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        private void UpdateSendStatistics(int numOfSentBytes)
        {
            SendBytesTotal += numOfSentBytes;

            int curr = DateTime.Now.Second;
            if (curr == CurrentSecondSend)
            {
                SendCurrentSecond += numOfSentBytes;
            }
            else
            {
                SendLastSecond = SendCurrentSecond;
                SendCurrentSecond = numOfSentBytes;
                CurrentSecondSend = curr;
            }
        }

        /// <summary>
        /// Update Receive Statistics
        /// </summary>
        /// <param name="numOfSentBytes"></param>
        private void UpdateReceiveStatistics(int numOfReceivedBytes)
        {
            //Statisktiken
            LastDataReceived = DateTime.Now;
            ReceivedBytesTotal += numOfReceivedBytes;

            int curr = DateTime.Now.Second;
            if (curr == CurrentSecondReceive)
            {
                ReceiveCurrentSecond += numOfReceivedBytes;
            }
            else
            {
                ReceiveLastSecond = ReceiveCurrentSecond;
                ReceiveCurrentSecond = numOfReceivedBytes;
                CurrentSecondReceive = curr;
            }
        }
    }
}
