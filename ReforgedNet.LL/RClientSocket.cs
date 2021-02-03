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

        private int DiscoverTransaction = -1;
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

            SendHello();
        }

        /// <summary>
        /// Close connection
        /// </summary>
        public void Disconnect()
        {
            //TODO: Send Disconnect Data

            if (Disconnected != null)
            {
                Disconnected();
            }
        }

        /// <summary>
        /// Sends Discover Message
        /// </summary>
        private void SendHello()
        {
            //Send discover message
            DiscoverTransaction = RTransactionGenerator.GenerateId();
            RNetMessage discover = new RNetMessage(null, new byte[0], DiscoverTransaction, RemoteEndPoint, RQoSType.Realiable);

            RegisterReceiver(null, OnDiscoverMessage);
            _outgoingMsgQueue.Enqueue(discover);
        }

        /// <summary>
        /// Discover-Answer arrived
        /// </summary>
        /// <param name="message"></param>
        private void OnDiscoverMessage(RNetMessage message)
        {
            if (message.TransactionId.Equals(DiscoverTransaction))
            {
                _logger?.WriteInfo(new LogInfo("Connection successful"));
                this.Connected = true;
                if (ConnectionSuccessful != null)
                {
                    ConnectionSuccessful();
                }
            }
        }
    }
}
