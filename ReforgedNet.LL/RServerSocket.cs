using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ReforgedNet.LL
{
    public class RServerSocket : RSocket
    {
        #region Events
        public delegate void ClientDiscoverMessageHandler(EndPoint ep);
        public event ClientDiscoverMessageHandler? ClientDiscoverMessage = null;

        public delegate void ClientDisconnectMessageHandler(EndPoint ep);
        public event ClientDisconnectMessageHandler? ClientDisconnectMessage = null;
        #endregion
        public RServerSocket(RSocketSettings settings, IPEndPoint remoteEndPoint, IPacketSerializer serializer, ILogger? logger) : base(settings, remoteEndPoint, serializer, logger)
        {
            
        }

        /// <summary>
        /// Starts listening on specified endpoint
        /// </summary>
        public void StartListen()
        {
            CreateSocket();
            if (_socket != null)
            {
                _socket?.Bind(RemoteEndPoint);

                _recvTask = Task.Factory.StartNew(() => ReceivingTask(_cts.Token), _cts.Token);
                _recvTask.ConfigureAwait(false);
                _sendTask = Task.Factory.StartNew(() => SendingTask(_cts.Token), _cts.Token);
                _sendTask.ConfigureAwait(false);

                RegisterReceiver(null, OnDiscoverMessage);
            }
        }

        private void OnDiscoverMessage(RNetMessage message)
        {
            string type = "discover";
            try
            {
                type = Encoding.UTF8.GetString(message.Data);
            }
            catch
            {

            }
            
            RNetMessage discover = new RNetMessage(null, Encoding.UTF8.GetBytes(type), message.TransactionId, message.RemoteEndPoint, RQoSType.Realiable);
            _outgoingMsgQueue.Enqueue(discover);

            if (type.Equals("disconnect"))
            {
                _logger?.WriteInfo(new LogInfo("Connection closed from " + message.RemoteEndPoint.ToString()));
                if (ClientDisconnectMessage != null)
                {
                    ClientDisconnectMessage(message.RemoteEndPoint);
                }
            }
            else
            {
                _logger?.WriteInfo(new LogInfo("Incomming connection from " + message.RemoteEndPoint.ToString()));
                if (ClientDiscoverMessage != null)
                {
                    ClientDiscoverMessage(message.RemoteEndPoint);
                }
            }
        }
    }
}
