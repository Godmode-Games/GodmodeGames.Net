using ReforgedNet.LL.Logging;
using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.LL
{
    public class RClientSocket : RSocket
    {
        public enum EDisconnectedBy { Client, Server, Timeout }

        public Action? Connected;
        public Action? ConnectFailed;
        public Action<EDisconnectedBy>? Disconnected;

        private long? DiscoverTransaction = null;
        private long? DisconnectTransation = null;
        private bool FireDisconnectAction = false;

        private bool FireConnectFailedAction = false;

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

            StartSendingTask();

            OnReceiveInternalData += OnInternalMessage;
            SendHello();
        }

        /// <summary>
        /// restarts receiver-/sending-task if they somehow crashed
        /// </summary>
        public override void Dispatch()
        {
            if (_socket == null)
            {
                return;
            }
            //Keep tasks running ...
            //StartReceiverTask();
            StartSendingTask();

            base.Dispatch();

            if (FireDisconnectAction) //invoke in main thread
            {
                FireDisconnectAction = false;
                Disconnected?.Invoke(EDisconnectedBy.Server);
                Close(); // close socket
            }
            if (FireConnectFailedAction)
            {
                FireConnectFailedAction = false;
                ConnectFailed?.Invoke();
                Close(); // close socket
            }
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
                FireConnectFailedAction = true;
                Error -= OnDiscoverFailed;
            }
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Disconnect()
        {
            if (IsConnected == false)
            {
                return;
            }

            SendDisconnect();

            DateTime start = DateTime.Now;
            while (IsConnected != false && DateTime.Now.Subtract(start).TotalMilliseconds < 2000)
            {
                Thread.Sleep(50);
            }

            Close(); //close socket

            Disconnected?.Invoke(EDisconnectedBy.Client);
        }

        /// <summary>
        /// Sends Discover Message
        /// </summary>
        private void SendHello()
        {
            DiscoverTransaction = RTransactionGenerator.GenerateId();

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
                //Disconnect answer from server - Action invoked in Disconnect-Method
                _logger?.WriteInfo(new LogInfo("Disconnect successful"));
                IsConnected = false;
                DisconnectTransation = null;
            }
            else if (type.Equals("disconnect_request") && IsConnected)
            {
                //Server requests a disconnect -> anwser confirm
                RNetMessage answer = new RNetMessage(Encoding.UTF8.GetBytes("disconnect_response"), message.TransactionId, RemoteEndPoint, RQoSType.Internal);
                _logger?.WriteInfo(new LogInfo("Disconnect by server"));

                IsConnected = false;
                DisconnectTransation = null;
                FireDisconnectAction = true; //Invoke action in Dispatch-method (on main thread)
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
            DisconnectTransation = RTransactionGenerator.GenerateId();
            RNetMessage disc = new RNetMessage(Encoding.UTF8.GetBytes("disconnect_request"), DisconnectTransation, RemoteEndPoint, RQoSType.Internal);
            _outgoingMsgQueue.Enqueue(disc);
        }
    }
}
