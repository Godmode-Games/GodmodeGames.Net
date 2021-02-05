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
        #region Events
        public delegate void ConnectionSuccessHandler();
        public event ConnectionSuccessHandler? ConnectionSuccessful;

        public delegate void ConnectionFailedHandler();
        public event ConnectionFailedHandler? ConnectionFailed;

        public delegate void DisconnectHandler();
        public event DisconnectHandler? Disconnected;
        #endregion

        private int? DiscoverTransaction = null;
        private int? DisconnectTransation = null;
        public bool Connected = false;

        public RClientSocket(RSocketSettings settings, IPacketSerializer serializer, ILogger? logger) : base(settings, new IPEndPoint(IPAddress.Any, 0), serializer, logger)
        {
            this.Connected = false;
        }

        /// <summary>
        /// Connecting to specified endpoint
        /// </summary>
        public void Connect(IPEndPoint ep)
        {
            RemoteEndPoint = ep;
            CreateSocket();

            _sendTask = Task.Factory.StartNew(() => SendingTask(_cts.Token), _cts.Token);
            _sendTask.ConfigureAwait(false);

            Connected = false;
            RegisterReceiver(null, OnDiscoverMessage);

            SendHello();
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Disconnect()
        {
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

            RNetMessage discover = new RNetMessage(null, Encoding.UTF8.GetBytes("discover"), DiscoverTransaction, RemoteEndPoint, RQoSType.Realiable, OnConnetionFailed);
            _outgoingMsgQueue.Enqueue(discover);
        }

        /// <summary>
        /// Sending Discover-Message failed
        /// </summary>
        private void OnConnetionFailed()
        {
            Connected = false;
            if (ConnectionFailed != null)
            {
                ConnectionFailed();
            }
        }

        /// <summary>
        /// Discover-Answer arrived
        /// </summary>
        /// <param name="message"></param>
        private void OnDiscoverMessage(RNetMessage message)
        {
            if ((DisconnectTransation != null && message.TransactionId.Equals(DisconnectTransation))
                || (DiscoverTransaction != null && message.TransactionId.Equals(DiscoverTransaction)))
            { 
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

                if (type.Equals("disconnect") && Connected)
                {
                    _logger?.WriteInfo(new LogInfo("Disconnect successful"));
                    Connected = false;
                    DisconnectTransation = null;

                    if (Disconnected != null)
                    {
                        Disconnected();
                    }
                }
                else if (!Connected)
                {
                    _logger?.WriteInfo(new LogInfo("Connection successful"));
                    Connected = true;
                    DiscoverTransaction = null;
                    if (ConnectionSuccessful != null)
                    {
                        ConnectionSuccessful();
                    }
                }
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

            RNetMessage disc = new RNetMessage(null, Encoding.UTF8.GetBytes("disconnect"), DisconnectTransation, RemoteEndPoint, RQoSType.Realiable, OnDisconnectFailed);
            //RegisterReceiver(null, OnDiscoverMessage); --> registered at connect
            _outgoingMsgQueue.Enqueue(disc);
        }

        /// <summary>
        /// Disconnect failed, never the less disconnect
        /// </summary>
        private void OnDisconnectFailed()
        {
            _logger?.WriteInfo(new LogInfo("Disconnect failed, cancel connection anyways"));
            Connected = false;
            DisconnectTransation = null;
            if (Disconnected != null)
            {
                Disconnected();
            }
        }
    }
}
