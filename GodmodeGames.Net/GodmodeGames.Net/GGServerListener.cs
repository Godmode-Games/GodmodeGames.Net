using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport;
using System;
using System.Collections.Generic;
using System.Net;
using static GodmodeGames.Net.Transport.IServerTransport;

namespace GodmodeGames.Net
{
    public class GGServerListener : GGSocket
    {
        /// <summary>
        /// Server-Settings
        /// </summary>
        public ServerSocketSettings Settings { get; private set; }
        /// <summary>
        /// Transport layer
        /// </summary>
        internal IServerTransport Transport = null;
        /// <summary>
        /// Connected clients
        /// </summary>
        public Dictionary<IPEndPoint, GGConnection> Connections => this.Transport.Connections;
        /// <summary>
        /// Is the server running?
        /// </summary>
        public bool IsListening => this.Transport != null ? this.Transport.ListeningStatus == EListeningStatus.Listening : false;

        #region Events
        public delegate void ClientConnectHandler(GGConnection connection);
        public event ClientConnectHandler ClientConnected;
        public delegate void ClientDisconnectHandler(GGConnection connection, string reason = null);
        public event ClientDisconnectHandler ClientDisconnected;
        public delegate void ShutdownCompleteHandler();
        public event ShutdownCompleteHandler ShutdownCompleted;
        public delegate void ReceivedMessageHandler(byte[] data, GGConnection connection);
        public event ReceivedMessageHandler ReceivedData;
        #endregion

        public GGServerListener(ServerSocketSettings settings, ILogger logger)
        {
            if (settings == null)
            {
                settings = new ServerSocketSettings();
            }

            this.Settings = settings;
            this.Logger = logger;

            if (this.Settings.Transport == SocketSettings.ETransport.Tcp)
            {
                throw new NotImplementedException("TCP TODO");
            }
            else
            {
                this.Transport = new Transport.Udp.UdpServerListener();
            }

            this.Transport.ClientConnected += this.OnClientConnect;
            this.Transport.ReceivedData += this.OnReceiveData;
            this.Transport.ClientDisconnected += this.OnClientDisconnect;
            this.Transport.ShutdownCompleted += OnShutdownCompleted;
        }

        /// <summary>
        /// Start listening on a port
        /// </summary>
        /// <param name="endpoint"></param>
        public void StartListening(IPEndPoint endpoint)
        {
            this.Logger?.LogInfo("Start listening on port " + endpoint.Port + " ...");
            this.Transport.Inititalize(this.Settings, this.Logger);
            this.Transport.StartListening(endpoint);
        }

        /// <summary>
        /// Disconnect all clients and shut down the server synchronous
        /// </summary>
        public bool Shutdown(string reason = "Shutdown")        
        {
            if (this.Transport != null)
            {
                return this.Transport.Shutdown(reason);
            }
            return false;
        }

        public void ShutdownAsync(string reason = "Shutdown")
        {
            this.Transport?.ShutdownAsync(reason);
        }

        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        public void Tick()
        {
            this.Transport.Tick();
        }

        /// <summary>
        /// send byte-array to connection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        public void Send(byte[] data, GGConnection connection)
        {
            this.Transport.Send(data, connection);
        }

        /// <summary>
        /// send byte-array to connection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, GGConnection connection, bool reliable)
        {
            this.Transport.Send(data, connection, reliable);
        }

        /// <summary>
        /// disconnect a client
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="reason"></param>
        public void DisconnectClient(GGConnection connection, string reason = null)
        {
            this.Transport.DisconnectClient(connection, reason);
        }

        /// <summary>
        /// Transport-layer received data from a connection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        private void OnReceiveData(byte[] data, GGConnection connection)
        {
            this.ReceivedData?.Invoke(data, connection);
        }

        /// <summary>
        /// transport-layer reports client connect
        /// </summary>
        /// <param name="connection"></param>
        private void OnClientConnect(GGConnection connection)
        {
            this.ClientConnected?.Invoke(connection);
        }

        /// <summary>
        /// transport-layer report client disconnect
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="reason"></param>
        private void OnClientDisconnect(GGConnection connection, string reason = null)
        {
            this.ClientDisconnected?.Invoke(connection, reason);
        }

        /// <summary>
        /// Transport-layer report shutdown completed
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        private void OnShutdownCompleted()
        {
            this.Transport.ClientConnected -= this.OnClientConnect;
            this.Transport.ReceivedData -= this.OnReceiveData;
            this.Transport.ClientDisconnected -= this.OnClientDisconnect;
            this.Transport.ShutdownCompleted -= OnShutdownCompleted;
            this.ShutdownCompleted?.Invoke();
        }
    }
}
