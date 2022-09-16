using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using GodmodeGames.Net.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using static GodmodeGames.Net.GGClient;
using static GodmodeGames.Net.Transport.IClientTransport;
using static GodmodeGames.Net.Transport.Udp.UdpMessage;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class UdpClient : UdpPeer, IClientTransport
    {
        public GGStatistics Statistics { get; set; } = new GGStatistics();
        public EConnectionStatus ConnectionStatus { get; set; } = EConnectionStatus.NotConnected;

        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method
        private List<int> ReceivedMessagesBuffer = new List<int>();

        private string DisconnectReason = null;
        private DateTime NextTickCheck = DateTime.UtcNow;

        /// <summary>
        /// When was the last heartbeat sent?
        /// </summary>
        internal DateTime LastHeartbeat = DateTime.UtcNow;
        /// <summary>
        /// Internal message of the last heartbeat-message
        /// </summary>
        internal int LastHeartbeatId = -1;
        /// <summary>
        /// stopwatch to calcualte rtt
        /// </summary>
        private Stopwatch HeartbeatStopwatch = new Stopwatch();

        /// <summary>
        /// Round Trip Time (Ping)
        /// </summary>
        public int RTT { get; set; } = -1;

        #region Events
        public event ReceiveDataHandler ReceivedData;
        public event ConnectAttemptHandler ConnectAttempt;
        public event DisconnectedHandler Disconnected;
        #endregion

        /// <summary>
        /// Inititalize socket
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        public void Inititalize(ClientSocketSettings settings, ILogger logger)
        {
            base.Initialize((SocketSettings)settings, logger);
            this.ConnectionStatus = EConnectionStatus.NotConnected;
            this.NextTickCheck = DateTime.UtcNow;
        }

        /// <summary>
        /// Connect asynchronous to server, check the ConnectAttempt-Event for result
        /// </summary>
        /// <param name="endpoint"></param>
        public void ConnectAsync(IPEndPoint endpoint)
        {
            this.RemoteEndpoint = endpoint;

            this.ConnectionStatus = EConnectionStatus.Connecting;

            UdpMessage discover = new UdpMessage()
            {
                MessageType = EMessageType.DiscoverRequest,
                MessageId = this.GetNextReliableId(),
                Data = new byte[0],
                RemoteEndpoint = this.RemoteEndpoint
            };

            this.Send(discover);
        }

        /// <summary>
        /// connect synchronous to server
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public bool Connect(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                this.Logger?.LogError("No server endpoint defined!");
                return false;
            }
            this.ConnectAsync(endpoint);

            DateTime start = DateTime.UtcNow;
            while (this.ConnectionStatus == EConnectionStatus.Connecting && DateTime.UtcNow.Subtract(start).TotalMilliseconds < 2000)
            {
                Thread.Sleep(10);
            }

            if (this.ConnectionStatus == EConnectionStatus.Connecting)
            {
                this.ConnectionStatus = EConnectionStatus.NotConnected;
                this.TickEvents.Enqueue(new TickEvent()
                {
                    OnTick = () =>
                    {
                        this.ConnectAttempt?.Invoke(false);
                    }
                });
                this.StopReceive();
            }

            return this.ConnectionStatus == EConnectionStatus.Connected;
        }

        /// <summary>
        /// Send data to the server, reliable or not
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, bool reliable = true)
        {
            if (data.Length == 0)
            {
                this.Logger?.LogWarning("Tried to send empty message.");
                return;
            }
            else if (this.RemoteEndpoint == null)
            {
                this.Logger?.LogError("Could not send message to undefined endpoint!");
                return;
            }

            if (!this.CTS.Token.IsCancellationRequested)
            {
                int msgid = -1;
                if (reliable == true)
                {
                    msgid = this.GetNextReliableId();
                }

                UdpMessage message = new UdpMessage(data, msgid, this.RemoteEndpoint, EMessageType.Data);
                this.Send(message);
            }
        }

        /// <summary>
        /// Send data to the server, reliablitity set by client settings
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            this.Send(data, this.SocketSettings != null && this.SocketSettings.UdpDefaultSendMode == EUdpSendMode.Reliable ? true : false);
        }

        /// <summary>
        /// send an internal message to server
        /// </summary>
        /// <param name="msg"></param>
        protected override void Send(UdpMessage msg)
        {
            base.Send(msg);
        }

        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        public void Tick()
        {
            this.DispatchMessages();
            //Keep tasks running ...
            this.StartSendingTask();

            while (this.TickEvents.TryDequeue(out TickEvent tick))
            {
                tick.OnTick?.Invoke();
            }

            if (this.NextTickCheck <= DateTime.UtcNow)
            {
                this.NextTickCheck = DateTime.UtcNow.AddMilliseconds(this.SocketSettings.TickCheckRate);

                if (this.LastHeartbeat.AddMilliseconds(this.SocketSettings.HeartbeatInterval) < DateTime.UtcNow)
                {
                    UdpMessage msg = new UdpMessage()
                    {
                        MessageType = EMessageType.HeartBeat,
                        MessageId = this.GetNextReliableId(),
                        Data = new byte[0],
                        RemoteEndpoint = this.RemoteEndpoint
                    };
                    this.LastHeartbeatId = msg.MessageId;
                    this.LastHeartbeat = DateTime.UtcNow;
                    this.HeartbeatStopwatch.Restart();

                    this.Send(msg);
                }

                //check timeout
                if (DateTime.UtcNow.Subtract(this.Statistics.LastDataReceived).TotalSeconds > this.SocketSettings.TimeoutTime)
                {
                    this.StopReceive();
                    this.Disconnected?.Invoke(EDisconnectBy.ConnectionLost, "timeout");
                }
            }
        }

        /// <summary>
        /// stop tasks
        /// </summary>
        private void StopReceive()
        {
            //Empty Queue
            this.OutgoingMessages.Clear();
            this.PendingUnacknowledgedMessages.Clear();
            this.ReceivedMessagesBuffer.Clear();
            this.PendingIncommingMessages.Clear();
            this.PendingOutgoingMessages.Clear();

            try
            {
                this.CTS.Cancel();
                this.SendTask?.Wait(500);
                this.ReceiveTask?.Wait(500);
                this.Socket?.Close();
                this.Socket = null;
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// Disconnect synchronous with a reason
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        public bool Disconnect(string reason = null)
        {
            this.DisconnectAsync(reason);

            DateTime start = DateTime.UtcNow;
            while (this.ConnectionStatus == EConnectionStatus.Disconnecting && DateTime.UtcNow.Subtract(start).TotalMilliseconds < 2000)
            {
                Thread.Sleep(10);
            }

            if (this.ConnectionStatus == EConnectionStatus.Disconnected)
            {
                this.StopReceive();
                this.ConnectionStatus = EConnectionStatus.Disconnected;
            }

            return this.ConnectionStatus == EConnectionStatus.Disconnected;
        }

        /// <summary>
        /// Disconnect asynchronous with a reason
        /// </summary>
        /// <param name="reason"></param>
        public void DisconnectAsync(string reason = null)
        {
            if (this.ConnectionStatus >= EConnectionStatus.Disconnecting)
            {
                return;
            }

            this.ConnectionStatus = EConnectionStatus.Disconnecting;
            this.DisconnectReason = reason;

            byte[] data = new byte[0];
            if (reason != null)
            {
                data = Encoding.UTF8.GetBytes(reason);
            }

            UdpMessage disc = new UdpMessage()
            {
                MessageType = EMessageType.Disconnect,
                MessageId = this.GetNextReliableId(),                
                Data = data,
                RemoteEndpoint = this.RemoteEndpoint
            };

            this.Send(disc);
        }

        /// <summary>
        /// invoke the receiveddata event in main loop
        /// </summary>
        protected void DispatchMessages()
        {
            if (!this.IncommingMessages.IsEmpty)
            {
                while (this.IncommingMessages.TryDequeue(out UdpMessage msg))
                {
                    this.DispatchMessage(msg);
                }
            }
        }

        /// <summary>
        /// Handle a message instantly, if ServerSocketSettings.InvokeReceiveDataEventOnTick == false
        /// </summary>
        /// <param name="msg"></param>
        protected override void DispatchMessage(UdpMessage msg)
        {
            if (this.ReceivedMessagesBuffer.Contains(msg.MessageId))
            {
                this.Logger?.LogInfo("Skipping already received message.");
                return;
            }
            else
            {
                this.ReceivedMessagesBuffer.Add(msg.MessageId);
                if (this.ReceivedMessagesBuffer.Count > this.SocketSettings.UdpDublicateMessagesBuffer)
                {
                    this.ReceivedMessagesBuffer.RemoveAt(0);
                }
            }

            //Data message
            this.ReceivedData?.Invoke(msg.Data);
        }

        /// <summary>
        /// Called by receiver-task, asynchronous, dispatch internal messages
        /// </summary>
        /// <param name="msg"></param>
        protected override void ReceivedInternalMessage(UdpMessage msg)
        {
            //internal messagess
            if (msg.MessageType == EMessageType.DiscoverResponse && this.ConnectionStatus == EConnectionStatus.Connecting)
            {
                //Discover successful
                this.ConnectionStatus = EConnectionStatus.Connected;
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.ConnectAttempt?.Invoke(true);
                    }
                });
                this.LastHeartbeat = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(this.SocketSettings.HeartbeatInterval - 1000)); //first heartbeat 1 second after connect
            }
            else if (msg.MessageType == EMessageType.Disconnect && this.ConnectionStatus > EConnectionStatus.NotConnected && this.ConnectionStatus < EConnectionStatus.Disconnected)
            {
                //Server wants to disconnect client
                this.ConnectionStatus = EConnectionStatus.Disconnected;

                string reason = Helper.BytesToString(msg.Data);
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(EDisconnectBy.Server, reason);
                        this.StopReceive();
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
            if (msg.MessageType == EMessageType.Disconnect && this.ConnectionStatus == EConnectionStatus.Disconnecting)
            {
                //Disconnect successful
                this.ConnectionStatus = EConnectionStatus.Disconnected;

                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(EDisconnectBy.Client, this.DisconnectReason);
                        this.StopReceive();
                    }
                });
            }
            else if (msg.MessageType == EMessageType.HeartBeat)
            {
                //got the heartbeat response
                if (this.LastHeartbeatId == msg.MessageId)
                {
                    this.HeartbeatStopwatch.Stop();
                    this.RTT = (int)this.HeartbeatStopwatch.ElapsedMilliseconds;

                    //reset
                    this.LastHeartbeatId = -1;
                    this.LastHeartbeat = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// update the received-statistics
        /// </summary>
        /// <param name="bytes"></param>
        protected override void UpdateStatisticsReceive(int bytes, IPEndPoint endpoint)
        {
            this.Statistics.UpdateReceiveStatistics(bytes);
        }

        /// <summary>
        /// update the sent-statistics
        /// </summary>
        /// <param name="bytes"></param>
        protected override void UpdateStatisticsSent(int bytes, IPEndPoint endpoint)
        {
            this.Statistics.UpdateSentStatistics(bytes);
        }

        /// <summary>
        /// update the statistic, because a packet was lost
        /// </summary>
        protected override void UpdatePacketLost(IPEndPoint endpoint)
        {
            this.Statistics.UpdatePacketLost();
        }

        /// <summary>
        /// No ACK-message was received in the certain time
        /// </summary>
        /// <param name="message"></param>
        protected override void PacketLost(AckMessage message)
        {
            if (message.MessageType == EMessageType.DiscoverRequest && this.ConnectionStatus == EConnectionStatus.Connecting)
            {
                this.StopReceive();
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.ConnectAttempt?.Invoke(false);
                    }
                });
            }
            else if (message.MessageType == EMessageType.Disconnect && this.ConnectionStatus == EConnectionStatus.Disconnecting)
            {
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(EDisconnectBy.Client, this.DisconnectReason);
                        this.StopReceive();
                    }
                });
            }
        }

        /// <summary>
        /// Socket connection to the server is lost
        /// </summary>
        /// <param name="endpoint"></param>
        protected override void ConnectionFailed(IPEndPoint endpoint)
        {
            if (this.ConnectionStatus != EConnectionStatus.Disconnected)
            {
                if (this.ConnectionStatus == EConnectionStatus.Connecting)
                {
                    this.TickEvents.Enqueue(new TickEvent
                    {
                        OnTick = () =>
                        {
                            this.ConnectAttempt?.Invoke(false);
                        }
                    });
                }
                this.TickEvents.Enqueue(new TickEvent
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(EDisconnectBy.ConnectionLost, null);
                        this.StopReceive();
                    }
                });
            }
        }
    }
}
