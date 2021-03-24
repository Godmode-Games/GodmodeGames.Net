using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReforgedNet.LL
{
    public class RClientSocket : RSocket
    {
        public Action? Connected;
        public Action? ConnectFailed;
        public Action<bool>? Disconnected;

        private long? DiscoverTransaction = null;
        private long? DisconnectTransation = null;
        public bool IsConnected { get; private set; } = false;

        public RClientSocket(RSocketSettings settings, IPacketSerializer serializer, ILogger? logger)
            : base(settings, new IPEndPoint(IPAddress.Any, 0), serializer, logger) { }

        /// <summary>
        /// Connecting to specified endpoint
        /// </summary>
        public void Connect(IPEndPoint ep)
        {
            RemoteEndPoint = ep;
            CreateSocket();

            _sendTask = Task.Factory.StartNew(() => SendingTask(_cts.Token), _cts.Token);
            _sendTask.ConfigureAwait(false);

            OnReceiveInternalData += this.OnInternalMessage;
            SendHello();
        }

        /// <summary>
        /// restarts receiver-/sending-task if somehow crashed
        /// </summary>
        public override void Dispatch()
        {
            //Keep tasks running ...
            if (_recvTask != null && _recvTask.Status != TaskStatus.Running)
            {
                StartReceiverTask();
            }
            if (_sendTask != null && _sendTask.Status != TaskStatus.Running)
            {
                StartSendingTask();
            }

            base.Dispatch();
        }

        /// <summary>
        /// Login failed
        /// </summary>
        /// <param name="tid"></param>
        private void OnDiscoverFailed(long tid)
        {
            if (tid == DiscoverTransaction)
            {
                IsConnected = false;
                ConnectFailed?.Invoke();
            }
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Disconnect(Action<bool>? disconnectCallback = null)
        {
            if (disconnectCallback != null)
            {
                Disconnected += disconnectCallback;
            }
            SendDisconnect();
        }

        /// <summary>
        /// Sends Discover Message
        /// </summary>
        private void SendHello()
        {
            //Send discover message
            if (DiscoverTransaction == null)
            {
                DiscoverTransaction = RTransactionGenerator.GenerateId();
            }

            Error += OnDiscoverFailed;

            RNetMessage discover = new RNetMessage(Encoding.UTF8.GetBytes("discover"), DiscoverTransaction, RemoteEndPoint, RQoSType.Internal);
            _outgoingMsgQueue.Enqueue(discover);
        }

        /// <summary>
        /// Discover-Answer arrived
        /// </summary>
        /// <param name="message"></param>
        private void OnInternalMessage(RNetMessage message)
        {
            if (message.RemoteEndPoint != RemoteEndPoint)
            {
                //not the server...
                return;
            }

            string type = "discover";
            try
            {
                type = Encoding.UTF8.GetString(message.Data);
            }
            catch
            {
                //Encoding error
                return;
            }

            if (type.Equals("disconnect_response") && IsConnected && DisconnectTransation == message.TransactionId)
            {
                //Disconnect answer from server
                _logger?.WriteInfo(new LogInfo("Disconnect successful"));
                IsConnected = false;
                DisconnectTransation = null;

                Disconnected?.Invoke(true);
                Error -= OnDisconnectFailed; //Clear all discover-fails
            }
            else if (type.Equals("disconnect_request") && IsConnected)
            {
                //Server requests a disconnect -> anwser confirm
                RNetMessage answer = new RNetMessage(Encoding.UTF8.GetBytes("disconnect_response"), message.TransactionId, RemoteEndPoint, RQoSType.Internal);
                _logger?.WriteInfo(new LogInfo("Disconnect by server"));

                IsConnected = false;
                DisconnectTransation = null;
                Disconnected?.Invoke(false);
            }
            else if (type.Equals("discover") && !IsConnected && DiscoverTransaction == message.TransactionId)
            {
                //Discover answer from server
                _logger?.WriteInfo(new LogInfo("Connection successful"));
                IsConnected = true;
                DiscoverTransaction = null;
                Error -= OnDiscoverFailed; //Clear all discover-fails

                Connected?.Invoke();
            }
        }

        /// <summary>
        /// Sends disconnect message
        /// </summary>
        private void SendDisconnect()
        {
            if (DisconnectTransation == null)
            {
                DisconnectTransation = RTransactionGenerator.GenerateId();
            }

            Error += OnDisconnectFailed;
            RNetMessage disc = new RNetMessage(Encoding.UTF8.GetBytes("disconnect_request"), DisconnectTransation, RemoteEndPoint, RQoSType.Internal);
            _outgoingMsgQueue.Enqueue(disc);
        }

        /// <summary>
        /// Disconnect failed, never the less disconnect
        /// </summary>
        private void OnDisconnectFailed(long tid)
        {
            if (tid == DisconnectTransation)
            {
                _logger?.WriteInfo(new LogInfo("Disconnect failed, cancel connection anyways"));
                IsConnected = false;
                DisconnectTransation = null;

                Disconnected?.Invoke(false);
            }
        }
    }
}
