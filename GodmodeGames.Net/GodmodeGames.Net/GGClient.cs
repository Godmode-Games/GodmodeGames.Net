using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using static GodmodeGames.Net.Transport.IClientTransport;

namespace GodmodeGames.Net
{
    public class GGClient : GGSocket
    {
        #region Events
        public delegate void OnConnectAttemptHandler(bool success);
        public event OnConnectAttemptHandler ConnectAttempt;
        public delegate void OnReceiveDataHandler(byte[] data);
        public event OnReceiveDataHandler ReceivedData;
        public delegate void DisconnectHandler(EDisconnectBy disconnect_by, string reason);
        public event DisconnectHandler Disconnected;
        #endregion

        /// <summary>
        /// who starts the disconnect
        /// </summary>
        public enum EDisconnectBy { Client = 1, Server, ConnectionLost }

        internal IClientTransport Transport = null;
        /// <summary>
        /// Client settings
        /// </summary>
        protected ClientSocketSettings Settings { get; private set; } = null;
        /// <summary>
        /// Server endpoint
        /// </summary>
        public IPEndPoint ServerEndpoint = null;
        /// <summary>
        /// is the client connected to server
        /// </summary>
        public bool IsConnected => this.Transport != null ? this.Transport.ConnectionStatus == EConnectionStatus.Connected : false;

        public GGClient(ClientSocketSettings settings = null, ILogger logger = null)
        {
            if (settings == null)
            {
                settings = new ClientSocketSettings();
            }

            this.Settings = settings;
            this.Logger = logger;

            if (this.Settings.Transport == SocketSettings.ETransport.Tcp)
            {
                //this.Transport = new Transport.Tcp.TcpClient();
                throw new NotImplementedException("TODO TCP");
            }
            else
            {
                this.Transport = new Transport.Udp.UdpClient();
            }

            this.Transport.Inititalize(this.Settings, this.Logger);
            this.Transport.ReceivedData += this.OnReceiveData;
        }

        /// <summary>
        /// connect synchronous to a host with a port
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public bool Connect(string host, int port)
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            return this.Connect(endpoint);
        }

        /// <summary>
        /// Connect synchronous to a server endpoint
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public bool Connect(IPEndPoint server)
        {
            this.ServerEndpoint = server;
            this.Logger?.LogInfo("Connecting to " + this.ServerEndpoint.ToString() + " ...");
            this.Transport.ConnectAttempt += this.OnTransportConnect;
            return this.Transport.Connect(server);
        }

        /// <summary>
        /// Connect asynchronous to a host with port, check ConnectAttempt-event for result
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        public void ConnectAsync(string host, int port)
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            this.ConnectAsync(endpoint);
        }

        /// <summary>
        /// Connect asynchronous to server endpoint, check ConnectAttempt-event for result
        /// </summary>
        /// <param name="server"></param>
        public void ConnectAsync(IPEndPoint server)
        {
            this.ServerEndpoint = server;
            this.Logger?.LogInfo("Connecting to " + this.ServerEndpoint.ToString() + " ...");
            this.Transport.ConnectAttempt += this.OnTransportConnect;
            this.Transport.ConnectAsync(server);
        }

        /// <summary>
        /// Disconnect synchronous with a reason
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        public bool Disconnect(string reason = null)
        {
            this.Logger?.LogInfo("Disconnecting from " + this.ServerEndpoint.ToString() + " ...");
            this.Transport.ReceivedData -= this.OnReceiveData;
            return this.Transport.Disconnect(reason);
        }

        /// <summary>
        /// Disconnect asynchronous with a reason, check Disconnected-event if disconnect is finished
        /// </summary>
        /// <param name="reason"></param>
        public void DisconnectAsync(string reason = null)
        {
            this.Logger?.LogInfo("Disconnecting from " + this.ServerEndpoint.ToString() + " ...");
            this.Transport.ReceivedData -= this.OnReceiveData;
            this.Transport.DisconnectAsync(reason);
        }

        /// <summary>
        /// Send data to server
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            this.Transport.Send(data);
        }

        /// <summary>
        /// Send data to server
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, bool reliable)
        {
            this.Transport.Send(data, reliable);
        }

        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        public void Tick()
        {
            this.Transport.Tick();
        }

        /// <summary>
        /// Transport-layer received data from server
        /// </summary>
        /// <param name="data"></param>
        private void OnReceiveData(byte[] data)
        {
            this.ReceivedData?.Invoke(data);
        }

        /// <summary>
        /// Transport-layer reports a connect-attempt to the server
        /// </summary>
        /// <param name="success"></param>
        private void OnTransportConnect(bool success)
        {
            if (success)
            {
                this.Logger?.LogInfo("Connection to " + this.ServerEndpoint.ToString() + " successful");
                this.Transport.Disconnected += this.OnTransportDisconnect;
            }
            else
            {
                this.Logger?.LogInfo("Connection to " + this.ServerEndpoint.ToString() + " failed");
            }
            this.Transport.ConnectAttempt -= this.OnTransportConnect;
            this.ConnectAttempt?.Invoke(success);
        }

        /// <summary>
        /// Transport-layer reports disconnect from server
        /// </summary>
        /// <param name="disconnect_by">by client or server</param>
        /// <param name="reason">reason</param>
        private void OnTransportDisconnect(EDisconnectBy disconnect_by, string reason = null)
        {
            this.Disconnected?.Invoke(disconnect_by, reason);
        }
    }
}
