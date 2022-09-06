using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using static GodmodeGames.Net.Transport.IServerTransport;
using static GodmodeGames.Net.Transport.Message;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class UdpServerListener : UdpPeer, IServerTransport
    {
        public EListeningStatus ListeningStatus { get; set; }

        public ServerStatistics Statistics { get; set; } = new ServerStatistics();
        public ConcurrentDictionary<IPEndPoint, GGConnection> Connections { get; set; } = new ConcurrentDictionary<IPEndPoint, GGConnection>();

        private ConcurrentDictionary<GGConnection, PendingDisconnect> PendingDisconnects = new ConcurrentDictionary<GGConnection, PendingDisconnect>();

        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method

        public event ClientConnectHandler ClientConnected;
        public event ClientDisconnectHandler ClientDisconnected;
        public event ReceivedMessageHandler ReceivedData;
        public event ShutdownCompleteHandler ShutdownCompleted;

        /// <summary>
        /// Inititalize socket
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        public void Inititalize(ServerSocketSettings settings, ILogger logger)
        {
            base.Initialize((SocketSettings)settings, logger);
            this.ListeningStatus = EListeningStatus.NotListening;
        }

        /// <summary>
        /// start listening on an port
        /// </summary>
        /// <param name="endpoint"></param>
        public void StartListening(IPEndPoint endpoint)
        {
            this.RemoteEndpoint = endpoint;
            this.Socket.Bind(endpoint);
            this.StartReceivingTask();
            this.StartSendingTask();
            this.ListeningStatus = EListeningStatus.Listening;
        }

        /// <summary>
        /// Disconnect all clients and then shut down the server synchronous
        /// </summary>
        /// <returns></returns>
        public bool Shutdown(string reason = "Shutdown")
        {
            this.ShutdownAsync(reason);

            DateTime start = DateTime.UtcNow;
            while (this.ListeningStatus == EListeningStatus.ShuttingDown && DateTime.UtcNow.Subtract(start).TotalMilliseconds < 10000)
            {
                Thread.Sleep(10);
            }

            if (this.ListeningStatus == EListeningStatus.ShuttingDown)
            {
                //Force disconnecting all clients
                this.StopReceive();
                this.Connections.Clear();
                this.ListeningStatus = EListeningStatus.Shutdown;
            }

            return this.ListeningStatus == EListeningStatus.Shutdown;
        }

        /// <summary>
        /// Disconnect all clients and then shut down the server asynchronous, check ShutdownComplete-event for result
        /// </summary>
        public void ShutdownAsync(string reason = "Shutdown")
        {
            this.ListeningStatus = EListeningStatus.ShuttingDown;
            foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in this.Connections)
            {
                this.DisconnectClient(kvp.Value, reason);
            }
        }

        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        public void Tick()
        {
            this.DispatchMessages();

            //invoke Tick events
            while (this.TickEvents.TryDequeue(out TickEvent tick))
            {
                tick.OnTick?.Invoke();
            }

            List<KeyValuePair<IPEndPoint, GGConnection>> timeout = new List<KeyValuePair<IPEndPoint, GGConnection>>();
            int heartbeat = ((ServerSocketSettings)this.SocketSettings).HeartbeatInterval;
            //disconnect timed out connections and send heartbeat
            foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in this.Connections)
            {
                if (kvp.Value.Statistics.LastDataReceived.AddMilliseconds(this.SocketSettings.TimeoutTime) < DateTime.UtcNow)
                {
                    timeout.Add(kvp);
                }

                if (kvp.Value.LastHeartbeat.AddMilliseconds(heartbeat) < DateTime.UtcNow)
                {
                    Message msg = new Message
                    {
                        MessageType = EMessageType.HeartBeat,
                        MessageId = this.GetNextReliableId(),
                        Data = BitConverter.GetBytes(kvp.Value.RTT),
                        RemoteEndpoint = kvp.Key
                    };
                    kvp.Value.StartHeartbeat(msg.MessageId);
                    this.Send(msg);
                }
            }
            if (timeout.Count > 0)
            {
                foreach (KeyValuePair<IPEndPoint, GGConnection> kvp in timeout)
                {
                    this.ClientDisconnected?.Invoke(kvp.Value, "timeout");
                    this.RemoveClient(kvp.Value);
                }
                this.Logger?.LogInfo(timeout.Count + " connections timed out.");
            }

            //Remove old pending disconnects and force disconnect
            Dictionary<GGConnection, PendingDisconnect> remove = new Dictionary<GGConnection, PendingDisconnect>();
            foreach (KeyValuePair<GGConnection, PendingDisconnect> kvp in this.PendingDisconnects)
            {
                if (DateTime.UtcNow.Subtract(kvp.Value.SendRequest).TotalMilliseconds > ((ServerSocketSettings)this.SocketSettings).UdpPendingDisconnectsTimeout)
                {
                    remove.Add(kvp.Key, kvp.Value);
                }
            }

            if (remove.Count > 0)
            {
                foreach (KeyValuePair<GGConnection, PendingDisconnect> kvp in remove)
                {
                    this.PendingDisconnects.Remove(kvp.Key, out _);
                    this.ClientDisconnected?.Invoke(kvp.Key, kvp.Value.Reason);
                    this.RemoveClient(kvp.Key);
                }
            }
        }

        /// <summary>
        /// Send data to a certain connection, relaible or not
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, GGConnection connection, bool reliable = true)
        {
            if (connection == null || connection.ClientEndpoint == null)
            {
                this.Logger?.LogWarning("Could not send message to undefined endpoint!");
                return;
            }
            
            if (data.Length == 0)
            {
                this.Logger?.LogWarning("Tried to send empty message.");
                return;
            }

            if (!this.CTS.Token.IsCancellationRequested)
            {
                this.StartSendingTask();
                this.StartReceivingTask();

                int msgid = -1;
                if (reliable == true)
                {
                    msgid = this.GetNextReliableId();
                }

                Message message = new Message(data, msgid, connection.ClientEndpoint, EMessageType.Data);
                this.OutgoingMessages.Enqueue(message);
            }
        }

        /// <summary>
        /// Send data to a certain connection, reliability set by server settings
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        public void Send(byte[] data, GGConnection connection)
        {
            this.Send(data, connection, this.SocketSettings != null && this.SocketSettings.UdpDefaultSendMode == EUdpSendMode.Reliable ? true : false);
        }

        /// <summary>
        /// Disconnect a client with a reason
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="reason"></param>
        public void DisconnectClient(GGConnection connection, string reason = null)
        {
            if (!this.PendingDisconnects.ContainsKey(connection))
            {
                PendingDisconnect pending = new PendingDisconnect
                {
                    SendRequest = DateTime.UtcNow,
                    Reason = reason
                };

                if (this.PendingDisconnects.TryAdd(connection, pending) == false)
                {
                    //intant remove
                    this.ClientDisconnected?.Invoke(connection, reason);
                    this.RemoveClient(connection);
                }
                else
                {
                    byte[] data = new byte[0];
                    if (reason != null)
                    {
                        data = Encoding.UTF8.GetBytes(reason);
                    }

                    Message disc = new Message
                    {
                        MessageType = EMessageType.Disconnect,
                        MessageId = this.GetNextReliableId(),
                        RemoteEndpoint = connection.ClientEndpoint,
                        Data = data
                    };
                    this.OutgoingMessages.Enqueue(disc);
                }
            }
        }

        /// <summary>
        /// Stop tasks and close socket
        /// </summary>
        private void StopReceive()
        {
            //Empty Queue
            this.OutgoingMessages.Clear();
            this.PendingUnacknowledgedMessages.Clear();
            this.PendingDisconnects.Clear();
            this.Connections.Clear();

            try
            {
                this.CTS.Cancel();
                this.SendTask?.Wait(500);
                this.ReceiveTask?.Wait(500);
                this.Socket?.Close();
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// invoke the ReceivedData-events with packets
        /// </summary>
        protected void DispatchMessages()
        {
            if (!this.IncommingMessages.IsEmpty)
            {
                while (this.IncommingMessages.TryDequeue(out Message msg))
                {
                    GGConnection client;
                    if (!this.Connections.TryGetValue(msg.RemoteEndpoint, out client))
                    {
                        this.Logger?.LogWarning("Skipping message from unknown receiver " + msg.RemoteEndpoint);
                        continue;
                    }

                    UdpConnection conn = client.Transport as UdpConnection;
                    if (conn.ReceivedMessagesBuffer.Contains(msg.MessageId))
                    {
                        //this.Logger?.LogInfo("Skipping already received message.");
                        continue;
                    }
                    else
                    {
                        conn.ReceivedMessagesBuffer.Add(msg.MessageId);
                        if (conn.ReceivedMessagesBuffer.Count > this.SocketSettings.UdpDublicateMessagesBuffer)
                        {
                            conn.ReceivedMessagesBuffer.RemoveAt(0);
                        }
                    }

                    this.ReceivedData?.Invoke(msg.Data, client);
                }
            }
        }

        /// <summary>
        /// Called by receiver-task, asynchronous, dispatch internal messages
        /// </summary>
        /// <param name="msg"></param>
        protected override void ReceivedInternalMessage(Message msg)
        {
            //called async in receive-task!
            GGConnection client = null;
            if (!this.Connections.TryGetValue(msg.RemoteEndpoint, out client))
            {
                client = new GGConnection(this, (ServerSocketSettings)this.SocketSettings, this.Logger, (IPEndPoint)msg.RemoteEndpoint);
                if (this.Connections.TryAdd(msg.RemoteEndpoint, client))
                {
                    this.TickEvents.Enqueue(new TickEvent
                    {
                        OnTick = () =>
                        {
                            this.ClientConnected?.Invoke(client);
                        }
                    });
                }                
            }

            UdpConnection conn = client.Transport as UdpConnection;
            if (conn.ReceivedMessagesBuffer.Contains(msg.MessageId))
            {
                return;
            }
            else
            {
                conn.ReceivedMessagesBuffer.Add(msg.MessageId);
                if (conn.ReceivedMessagesBuffer.Count > this.SocketSettings.UdpDublicateMessagesBuffer)
                {
                    conn.ReceivedMessagesBuffer.RemoveAt(0);
                }
            }

            //internal message
            if (msg.MessageType == EMessageType.DiscoverRequest && this.ListeningStatus > EListeningStatus.NotListening && this.ListeningStatus < EListeningStatus.ShuttingDown)
            {
                Message ret = new Message { MessageType = EMessageType.DiscoverResponse, MessageId = this.GetNextReliableId(), RemoteEndpoint = msg.RemoteEndpoint };
                this.OutgoingMessages.Enqueue(ret);
            }
            else if (msg.MessageType == EMessageType.Disconnect)
            {
                //Client wants to disconnect
                string reason = null;
                if (msg.Data.Length > 0)
                {
                    try
                    {
                        reason = Encoding.UTF8.GetString(msg.Data);
                    }
                    catch
                    {

                    }
                }

                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.ClientDisconnected?.Invoke(client, reason);
                        this.RemoveClient(client);
                    }
                });
            }
        }

        /// <summary>
        /// Called by receiver-task, asynchronous, received a ack for internal message
        /// </summary>
        /// <param name="msg"></param>
        protected override void ReceivedInternalACK(AckMessage msg)
        {
            GGConnection client = null;
            if (!this.Connections.TryGetValue(msg.RemoteEndpoint, out client))
            {
                //no connection
                return;
            }

            UdpConnection conn = client.Transport as UdpConnection;

            if (msg.MessageType == EMessageType.Disconnect)
            {
                //client received the disconnect message from server
                if (this.PendingDisconnects.ContainsKey(client))
                {
                    string reason = null;
                    if (msg.Data.Length > 5)
                    {
                        try
                        {
                            reason = Encoding.UTF8.GetString(msg.Data);
                        }
                        catch
                        {

                        }
                    }

                    this.PendingDisconnects.Remove(client, out _);
                    if (this.ListeningStatus != EListeningStatus.ShuttingDown)
                    {
                        this.TickEvents.Enqueue(new TickEvent
                        {
                            OnTick = () =>
                            {
                                this.ClientDisconnected?.Invoke(client, reason);
                                this.RemoveClient(client);
                            }
                        });
                    }
                    else
                    {
                        //don't invoke event on shut down
                        this.RemoveClient(client);
                    }
                }
            }
            else if (msg.MessageType == EMessageType.HeartBeat)
            {
                //heartbeat response
                client.HeartbeatResponseReceived(msg.MessageId);
            }
        }

        /// <summary>
        /// Remove a client from connection list, execute ShutdownComplete-event if needed
        /// </summary>
        /// <param name="connection"></param>
        private void RemoveClient(GGConnection connection)
        {
            this.Connections.TryRemove(connection.ClientEndpoint, out _);

            if (this.ListeningStatus == EListeningStatus.ShuttingDown && this.Connections.Count == 0)
            {
                this.ListeningStatus = EListeningStatus.Shutdown;
                this.ShutdownCompleted?.Invoke();
            }
        }

        /// <summary>
        /// A message could not be send to a connection
        /// </summary>
        /// <param name="message"></param>
        protected override void PacketLost(AckMessage message)
        {
            if (message.MessageType == EMessageType.Disconnect)
            {
                if (this.Connections.TryGetValue(message.RemoteEndpoint, out GGConnection conn))
                {
                    if (this.PendingDisconnects.ContainsKey(conn))
                    {
                        string reason = null;
                        if (message.Data.Length > 0)
                        {
                            try
                            {
                                reason = Encoding.UTF8.GetString(message.Data);
                            }
                            catch
                            {

                            }
                        }

                        this.PendingDisconnects.Remove(conn, out _);
                        this.TickEvents.Enqueue(new TickEvent
                        {
                            OnTick = () =>
                            {
                                this.ClientDisconnected?.Invoke(conn, reason);
                                this.RemoveClient(conn);
                            }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// update the statistic, because a packet was lost
        /// </summary>
        protected override void UpdatePacketLost(IPEndPoint endpoint)
        {
            this.Statistics.UpdatePacketLost();
            if (this.Connections.TryGetValue(endpoint, out GGConnection conn))
            {
                conn.Statistics.UpdatePacketLost();
            }
        }

        /// <summary>
        /// update the received-statistics
        /// </summary>
        /// <param name="bytes"></param>
        protected override void UpdateStatisticsReceive(int bytes, IPEndPoint endpoint)
        {
            this.Statistics.UpdateReceiveStatistics(bytes);
            if (this.Connections.TryGetValue(endpoint, out GGConnection conn))
            {
                conn.Statistics.UpdateReceiveStatistics(bytes);
            }
        }

        /// <summary>
        /// update the sent-statistics
        /// </summary>
        /// <param name="bytes"></param>
        protected override void UpdateStatisticsSent(int bytes, IPEndPoint endpoint)
        {
            this.Statistics.UpdateSentStatistics(bytes);
            if (this.Connections.TryGetValue(endpoint, out GGConnection conn))
            {
                conn.Statistics.UpdateSentStatistics(bytes);
            }
        }

        /// <summary>
        /// Socket connection to a client is lost
        /// </summary>
        /// <param name="endpoint"></param>
        protected override void ConnectionFailed(IPEndPoint endpoint)
        {
            if (this.Connections.TryGetValue(endpoint, out GGConnection conn))
            {
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.ClientDisconnected?.Invoke(conn, "broken pipe");
                        this.RemoveClient(conn);
                    }
                });
            }
        }
    }
}
