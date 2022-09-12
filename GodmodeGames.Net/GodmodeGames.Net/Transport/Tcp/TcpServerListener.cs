using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
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
        public GGStatistics Statistics { get; set; } = new GGStatistics();
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
        internal ServerSocketSettings SocketSettings;
        internal ILogger Logger;

        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method

        /// <summary>
        /// Messages received, but not processed
        /// </summary>
        private ConcurrentQueue<TcpMessage> IncommingMessages = new ConcurrentQueue<TcpMessage>();
        /// <summary>
        /// Messages that should be sent
        /// </summary>
        private ConcurrentQueue<TcpMessage> OutgoingMessages = new ConcurrentQueue<TcpMessage>();
        /// <summary>
        /// Messages that should be send with a simulated ping
        /// </summary>
        protected ConcurrentQueue<TcpMessage> PendingOutgoingMessages = new ConcurrentQueue<TcpMessage>();
        /// <summary>
        /// Messages that should be received with a simulated ping
        /// </summary>
        protected ConcurrentQueue<TcpMessage> PendingIncommingMessages = new ConcurrentQueue<TcpMessage>();
        private Task SendTask = null;
        private CancellationTokenSource CTS = null;

        private int NextPingId = 1;
        private DateTime NextTickCheck = DateTime.UtcNow;

        /// <summary>
        /// Initialize the server
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        public void Inititalize(ServerSocketSettings settings, ILogger logger)
        {
            this.SocketSettings = settings;
            this.Logger = logger;
            this.ListeningStatus = EListeningStatus.NotListening;
            this.NextTickCheck = DateTime.UtcNow;
        }

        /// <summary>
        /// Start listening on a port
        /// </summary>
        /// <param name="endpoint"></param>
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

        /// <summary>
        /// send byte-array to connection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        public void Send(byte[] data, GGConnection connection)
        {
            TcpMessage message = new TcpMessage()
            {
                Data = data,
                Client = connection,
                MessageType = TcpMessage.EMessageType.Data
            };

            this.Send(message);
        }

        /// <summary>
        /// send byte-array to connection
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, GGConnection connection, bool reliable = true)
        {
            this.Send(data, connection);
        }

        /// <summary>
        /// Shut the server down, disconnect all clients with a reason
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        public bool Shutdown(string reason = "Shutdown")
        {
            this.ShutdownAsync(reason);

            return true;
        }

        /// <summary>
        /// Shut the server down, disconnect all clients with a reason, check the ShutdownCompleted-event for finish
        /// </summary>
        /// <param name="reason"></param>
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

        /// <summary>
        /// Disconnect a client connection with a reason
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="reason"></param>
        public void DisconnectClient(GGConnection connection, string reason = null)
        {
            ((TcpConnection)connection.Transport).Disconnect(reason);
        }

        /// <summary>
        /// have to be executed in update loop
        /// </summary>
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
                        TcpMessage msg = new TcpMessage()
                        {
                            MessageType = EMessageType.HeartBeatPing,
                            Data = BitConverter.GetBytes(pingid),
                            Client = kvp.Value
                        };
                        kvp.Value.StartHeartbeat(pingid);
                        this.Send(msg);
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

        /// <summary>
        /// Send an internal message to client
        /// </summary>
        /// <param name="msg"></param>
        internal void Send(TcpMessage msg)
        {
            if (this.SocketSettings.SimulatedPingOutgoing > 0 && msg.SimulatedPingAdded == false)
            {
                msg.SetPing(this.SocketSettings.SimulatedPingOutgoing);
                this.PendingOutgoingMessages.Enqueue(msg);
            }
            else
            {
                this.OutgoingMessages.Enqueue(msg);
            }

            this.StartSendingTask();
        }

        /// <summary>
        /// Callback, when a client connects
        /// </summary>
        /// <param name="ar"></param>
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

        /// <summary>
        /// starts the sending task, if not running
        /// </summary>
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
                                this.InternalSendTo(msg);
                            }
                        }

                        //Resend pending outgoing messages
                        if (this.Socket != null && this.PendingOutgoingMessages.Count > 0 && !this.CTS.IsCancellationRequested)
                        {
                            List<TcpMessage> new_pending = new List<TcpMessage>();
                            while (this.PendingOutgoingMessages.TryDequeue(out TcpMessage resend))
                            {
                                if (resend.ExecuteTime <= DateTime.UtcNow)
                                {
                                    this.InternalSendTo(resend);
                                }
                                else
                                {
                                    new_pending.Add(resend);
                                }
                            }
                            if (new_pending.Count > 0)
                            {
                                foreach (TcpMessage msg in new_pending)
                                {
                                    this.PendingOutgoingMessages.Enqueue(msg);
                                }
                            }
                        }

                        //Dispatch pending incomming messages
                        if (this.Socket != null && this.PendingIncommingMessages.Count > 0 && !this.CTS.IsCancellationRequested)
                        {
                            List<TcpMessage> new_pending = new List<TcpMessage>();
                            while (this.PendingIncommingMessages.TryDequeue(out TcpMessage resend))
                            {
                                if (resend.ExecuteTime <= DateTime.UtcNow)
                                {
                                    this.ProcessReceivedMessage(resend);
                                }
                                else
                                {
                                    new_pending.Add(resend);
                                }
                            }

                            if (new_pending.Count > 0)
                            {
                                foreach (TcpMessage msg in new_pending)
                                {
                                    this.PendingIncommingMessages.Enqueue(msg);
                                }
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

        /// <summary>
        /// Send the internal message to the client
        /// </summary>
        /// <param name="msg"></param>
        private void InternalSendTo(TcpMessage msg)
        {
            if (this.SocketSettings.SimulatedPingOutgoing > 0 && msg.SimulatedPingAdded == false)
            {
                msg.SetPing(this.SocketSettings.SimulatedPingOutgoing);
                this.PendingOutgoingMessages.Enqueue(msg);
                return;
            }

            if (msg.ExecuteTime > DateTime.UtcNow)
            {
                //don't process yet
                return;
            }

            byte[] data = msg.Serialize();
            this.Statistics.UpdateSentStatistics(data.Length);
            ((TcpConnection)msg.Client.Transport).SendToClient(data);

            return ;
        }

        /// <summary>
        /// called, when a client receives data 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="client"></param>
        internal void ClientDataReceived(byte[] data, GGConnection client)
        {
            this.Statistics.UpdateReceiveStatistics(data.Length);

            TcpMessage msg = new TcpMessage();
            if (msg.Deserialize(data, client))
            {
                this.ProcessReceivedMessage(msg);
            }
        }

        /// <summary>
        /// Process the received message
        /// </summary>
        /// <param name="msg"></param>
        private void ProcessReceivedMessage(TcpMessage msg)
        {
            if (this.SocketSettings.SimulatedPingIncomming > 0 && msg.SimulatedPingAdded == false)
            {
                msg.SetPing(this.SocketSettings.SimulatedPingIncomming);
                this.PendingIncommingMessages.Enqueue(msg);
                return;
            }

            if (msg.ExecuteTime > DateTime.UtcNow)
            {
                //don't process yet
                return;
            }

            if (msg.MessageType == TcpMessage.EMessageType.Data)
            {
                this.IncommingMessages.Enqueue(msg);
            }
            else
            {
                this.ReceivedInternalMessage(msg, msg.Client);
            }

            return;
        }

        /// <summary>
        /// Process an internal message
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="client"></param>
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
                    TcpMessage pong = new TcpMessage()
                    {
                        Data = msg.Data,
                        Client = client,
                        MessageType = EMessageType.HeartbeatPong
                    };
                    this.InternalSendTo(pong);
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

        /// <summary>
        /// Remove client from Connections-List and invoke ClientDisconnected-event
        /// </summary>
        /// <param name="client"></param>
        /// <param name="reason"></param>
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
            this.IncommingMessages.Clear();
            this.PendingIncommingMessages.Clear();
            this.PendingOutgoingMessages.Clear();
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
