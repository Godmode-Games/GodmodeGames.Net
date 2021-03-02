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
        /// <summary>
        /// Endpoints, that should be disconnected
        /// </summary>
        private Dictionary<EndPoint, DateTime> _pendingDisconnects = new Dictionary<EndPoint, DateTime>();

        #region Events
        public delegate void ClientDiscoverMessageHandler(EndPoint ep);
        public event ClientDiscoverMessageHandler? ClientDiscoverMessage = null;

        public delegate void ClientDisconnectHandler(EndPoint ep);
        public event ClientDisconnectHandler? ClientDisconnect = null;
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

            RNetMessage? discover = null;

            if (type.Equals("disconnect_request"))
            {
                //Client requests disconnect - send response
                _logger?.WriteInfo(new LogInfo("Connection closed by " + message.RemoteEndPoint.ToString()));
                discover = new RNetMessage(null, Encoding.UTF8.GetBytes("disconncet_response"), message.TransactionId, message.RemoteEndPoint, RQoSType.Realiable);                
                ClientDisconnect?.Invoke(message.RemoteEndPoint);
            }
            else if (type.Equals("disconnect_response"))
            {
                //Client answers for requested disconnect
                if (_pendingDisconnects.ContainsKey(message.RemoteEndPoint))
                {
                    _logger?.WriteInfo(new LogInfo("Connection to " + message.RemoteEndPoint + " closed by server"));
                    _pendingDisconnects.Remove(message.RemoteEndPoint);
                    ClientDisconnect?.Invoke(message.RemoteEndPoint);
                }
            }
            else if (type.Equals("discover"))
            {
                _logger?.WriteInfo(new LogInfo("Incomming connection from " + message.RemoteEndPoint.ToString()));
                ClientDiscoverMessage?.Invoke(message.RemoteEndPoint);
                discover = new RNetMessage(null, Encoding.UTF8.GetBytes("discover"), message.TransactionId, message.RemoteEndPoint, RQoSType.Realiable);
            }

            if (discover != null)
            {
                _outgoingMsgQueue.Enqueue(discover);
            }
        }

        public void DisconnectEndPointAsync(EndPoint ep)
        {
            if (!_pendingDisconnects.ContainsKey(ep))
            {
                RNetMessage disc = new RNetMessage(null, Encoding.UTF8.GetBytes("disconnect_request"), RTransactionGenerator.GenerateId(), ep, RQoSType.Realiable); ;
                _pendingDisconnects.Add(ep, DateTime.Now);
                _outgoingMsgQueue.Enqueue(disc);
            }
        }

        public new void Dispatch()
        {
            base.Dispatch();

            //remove pending disconnect while timeout
            if (_pendingDisconnects.Count > 0)
            { 
                List<EndPoint> removeEndPoint = new List<EndPoint>();
                foreach (KeyValuePair<EndPoint, DateTime> kvp in _pendingDisconnects)
                {
                    if (DateTime.Now.Subtract(kvp.Value).TotalMilliseconds > _settings.PendingDisconnectTimeout)
                    {
                        removeEndPoint.Add(kvp.Key);
                    }
                }

                foreach (EndPoint ep in removeEndPoint)
                {
                    _logger?.WriteInfo(new LogInfo("Disconnect timeout for " + ep.ToString() + " - force disconnect"));
                    ClientDisconnect?.Invoke(ep);
                    _pendingDisconnects.Remove(ep);
                }
            }
        }
    }
}
