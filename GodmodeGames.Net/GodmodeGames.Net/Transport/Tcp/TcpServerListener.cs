using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using GodmodeGames.Net.Transport.Udp;
using GodmodeGames.Net.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using static GodmodeGames.Net.Transport.IServerTransport;
using static GodmodeGames.Net.Transport.Tcp.TcpMessage;

namespace GodmodeGames.Net.Transport.Tcp
{
    internal class TcpServerListener : IServerTransport
    {
        public EListeningStatus ListeningStatus { get; set; } = EListeningStatus.NotListening;
        public ServerStatistics Statistics { get; set; } = new ServerStatistics();
        public ConcurrentDictionary<IPEndPoint, GGConnection> Connections { get; set; } = new ConcurrentDictionary<IPEndPoint, GGConnection>();

        #region Events
        public event ClientConnectHandler ClientConnected;
        public event ClientDisconnectHandler ClientDisconnected;
        public event ReceivedMessageHandler ReceivedData;
        public event ShutdownCompleteHandler ShutdownCompleted;
        #endregion

        internal X509Certificate ServerCertificate = null;
        private IPEndPoint ServerEndpoint;
        private TcpListener Socket = null;
        private ServerSocketSettings SocketSettings;
        internal ILogger Logger;

        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method

        private ConcurrentQueue<TcpMessage> IncommingMessages = new ConcurrentQueue<TcpMessage>();
        private ConcurrentQueue<TcpMessage> OutgoingMessages = new ConcurrentQueue<TcpMessage>();
        private Task SendTask = null;
        private CancellationTokenSource CTS = null;

        private int NextPingId = 1;
        private DateTime NextTickCheck = DateTime.UtcNow;

        public void Inititalize(ServerSocketSettings settings, ILogger logger)
        {
            this.SocketSettings = settings;
            this.Logger = logger;
            this.ListeningStatus = EListeningStatus.NotListening;
            this.NextTickCheck = DateTime.UtcNow;
        }

        public void StartListening(IPEndPoint endpoint)
        {
            this.CTS = new CancellationTokenSource();
            this.ServerEndpoint = endpoint;

            if (this.SocketSettings.TcpSSL == true)
            {
                if (string.IsNullOrEmpty(this.SocketSettings.TcpSSLCert))
                {
                    this.Logger?.LogError("Trying to use tcp over ssl, but no certificate is defined!");
                    return;
                }
            }

            if (this.SocketSettings.TcpSSL == true && this.SocketSettings.TcpSSLCert != null)
            {
                this.ServerCertificate = new X509Certificate2(this.SocketSettings.TcpSSLCert, this.SocketSettings.TcpSSLCertPassword);
            }            
            else if (this.SocketSettings.TcpSSL == true)
            {
                this.Logger?.LogError("No ssl certificate location specified!");
                return;
            }
            this.Socket = new TcpListener(this.ServerEndpoint);
            this.Socket.Start();
            this.Socket.BeginAcceptTcpClient(this.OnClientConnect, null);

            this.ListeningStatus = EListeningStatus.Listening;
        }

        public void Send(byte[] data, GGConnection connection)
        {
            TcpMessage message = new TcpMessage
            {
                Data = data,
                Client = connection,
                MessageType = TcpMessage.EMessageType.Data
            };
            this.OutgoingMessages.Enqueue(message);

            this.StartSendingTask();
        }

        public void Send(byte[] data, GGConnection connection, bool reliable = true)
        {
            this.Send(data, connection);
        }

        public bool Shutdown(string reason = "Shutdown")
        {
            this.ShutdownAsync(reason);

            return true;
        }

        public void ShutdownAsync(string reason)
        {
            this.ListeningStatus = EListeningStatus.ShuttingDown;

            if (this.Connections.Count > 0)
            {
                foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in this.Connections)
                {
                    this.DisconnectClient(kvp.Value, reason);
                }
            }

            this.StopReceive();

            this.ListeningStatus = EListeningStatus.Shutdown;
            this.ShutdownCompleted?.Invoke();
        }

        public void DisconnectClient(GGConnection connection, string reason = null)
        {
            ((TcpConnection)connection.Transport).Disconnect(reason);
        }

        public void Tick()
        {
            //Dispatcher messages
            if (!this.IncommingMessages.IsEmpty)
            {
                while (this.IncommingMessages.TryDequeue(out TcpMessage msg))
                {
                    this.ReceivedData?.Invoke(msg.Data, msg.Client);
                }
            }

            //invoke Tick events
            while (this.TickEvents.TryDequeue(out TickEvent tick))
            {
                tick.OnTick?.Invoke();
            }

            if (this.NextTickCheck <= DateTime.UtcNow)
            {
                this.NextTickCheck = DateTime.UtcNow.AddMilliseconds(this.SocketSettings.TickCheckRate);

                List<KeyValuePair<IPEndPoint, GGConnection>> timeout = new List<KeyValuePair<IPEndPoint, GGConnection>>();
                int heartbeat = this.SocketSettings.HeartbeatInterval;
                //disconnect timed out connections and send heartbeat
                foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in this.Connections)
                {
                    if (kvp.Value.Statistics.LastDataReceived.AddMilliseconds(this.SocketSettings.TimeoutTime) < DateTime.UtcNow)
                    {
                        timeout.Add(kvp);
                    }

                    if (kvp.Value.LastHeartbeat.AddMilliseconds(heartbeat) < DateTime.UtcNow)
                    {
                        int pingid = this.GetNextPingId();
                        TcpMessage msg = new TcpMessage
                        {
                            MessageType = EMessageType.HeartBeatPing,
                            Data = BitConverter.GetBytes(pingid),
                            Client = kvp.Value
                        };
                        kvp.Value.StartHeartbeat(pingid);
                        this.OutgoingMessages.Enqueue(msg);
                    }
                }
                if (timeout.Count > 0)
                {
                    foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in timeout)
                    {
                        this.RemoveClient(kvp.Value, "timeout");
                    }
                    this.Logger?.LogInfo(timeout.Count + " connections timed out.");
                }
            }
        }

        private void OnClientConnect(IAsyncResult ar)
        {
            if (this.Socket == null)
            {
                return;
            }
            System.Net.Sockets.TcpClient client = null;
            try
            {
                client = this.Socket.EndAcceptTcpClient(ar);
            }
            catch
            {
                return;
            }

            client.NoDelay = true;
            this.Socket.BeginAcceptTcpClient(this.OnClientConnect, null);

            GGConnection conn = new GGConnection(this, this.SocketSettings, this.Logger, (IPEndPoint) client.Client.RemoteEndPoint);
            if (((TcpConnection)conn.Transport).Initialize(client, this))
            {
                if (this.Connections.TryAdd((IPEndPoint)client.Client.RemoteEndPoint, conn))
                {
                    this.TickEvents.Enqueue(new TickEvent
                    {
                        OnTick = () =>
                        {
                            this.ClientConnected?.Invoke(conn);
                        }
                    });
                }
            }            
        }

        private void StartSendingTask()
        {
            if (this.Socket == null)
            {
                return;
            }

            if (this.SendTask == null || this.SendTask.IsCompleted == true)
            {
                this.SendTask = Task.Factory.StartNew(() =>
                {
                    while (!this.OutgoingMessages.IsEmpty || !this.CTS.IsCancellationRequested)
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        //Send Messages...
                        if (this.Socket != null && !this.OutgoingMessages.IsEmpty)
                        {
                            while (this.OutgoingMessages.TryDequeue(out TcpMessage msg))
                            {
                                byte[] data = msg.Serialize();
                                this.Statistics.UpdateSentStatistics(data.Length);
                                msg.Client.Statistics.UpdateSentStatistics(data.Length);
                                ((TcpConnection)msg.Client.Transport).SendToClient(data);
                            }
                        }

                        // if nothing to do, take a short break.
                        stopwatch.Stop();

                        var delay = this.SocketSettings.SendTickrate - stopwatch.ElapsedMilliseconds;
                        if (!this.CTS.IsCancellationRequested && delay > 0)
                        {
                            Thread.Sleep((int)delay);
                        }
                    }
                });
                this.SendTask.ConfigureAwait(false);
            }
        }

        internal void ClientDataReceived(byte[] data, GGConnection client)
        {
            client.Statistics.UpdateReceiveStatistics(data.Length);
            this.Statistics.UpdateReceiveStatistics(data.Length);

            TcpMessage msg = new TcpMessage();
            if (msg.Deserialize(data, client))
            {
                if (msg.MessageType == TcpMessage.EMessageType.Data)
                {
                    this.IncommingMessages.Enqueue(msg);
                }
                else
                {
                    this.ReceivedInternalMessage(msg, client);
                }
            }
        }

        private void ReceivedInternalMessage(TcpMessage msg, GGConnection client)
        {
            if (msg.MessageType == TcpMessage.EMessageType.Disconnect)
            {
                //client sends reason for his disconnect
                string reason = Helper.BytesToString(msg.Data);
                this.RemoveClient(client, reason);
            }
            else if (msg.MessageType == EMessageType.HeartBeatPing)
            {
                //client send heartbeat 
                if (msg.Data.Length >= 4)
                {
                    TcpMessage pong = new TcpMessage
                    {
                        Data = msg.Data,
                        Client = client,
                        MessageType = EMessageType.HeartbeatPong
                    };
                    this.OutgoingMessages.Enqueue(pong);
                }
            }
            else if (msg.MessageType == EMessageType.HeartbeatPong)
            {
                //got the heartbeat response from client
                if (msg.Data.Length >= 4)
                {
                    client.HeartbeatResponseReceived(BitConverter.ToInt32(msg.Data));
                }
            }
        }

        internal void RemoveClient(GGConnection client, string reason)
        {
            if (this.Connections.TryRemove(client.ClientEndpoint, out _))
            {
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.ClientDisconnected?.Invoke(client, reason);
                    }
                });
            }
        }

        /// <summary>
        /// Stop tasks and close socket
        /// </summary>
        private void StopReceive()
        {
            //Empty Queue
            this.OutgoingMessages.Clear();
            this.Connections.Clear();

            try
            {
                this.CTS.Cancel();
                this.SendTask?.Wait(500);
                this.Socket.Stop();
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// get the next reliable id
        /// </summary>
        /// <returns></returns>
        protected int GetNextPingId()
        {
            int id = this.NextPingId++;
            if (this.NextPingId > int.MaxValue)
            {
                this.NextPingId = 1;
            }

            return id;
        }
    }
}
