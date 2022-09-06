﻿using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using static GodmodeGames.Net.GGClient;
using static GodmodeGames.Net.Transport.IClientTransport;
using static GodmodeGames.Net.Transport.Message;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class UdpClient : UdpPeer, IClientTransport
    {
        public ClientStatistics Statistics { get; set; } = new ClientStatistics();
        public EConnectionStatus ConnectionStatus { get; set; } = EConnectionStatus.NotConnected;

        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method
        private List<int> ReceivedMessagesBuffer = new List<int>();

        private string DisconnectReason = null;

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
        }

        /// <summary>
        /// Connect asynchronous to server
        /// </summary>
        /// <param name="endpoint"></param>
        public void ConnectAsync(IPEndPoint endpoint)
        {
            this.RemoteEndpoint = endpoint;

            this.ConnectionStatus = EConnectionStatus.Connecting;

            Message discover = new Message
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
            this.ConnectAsync(endpoint);

            DateTime start = DateTime.UtcNow;
            while (this.ConnectionStatus == EConnectionStatus.Connecting && DateTime.UtcNow.Subtract(start).TotalMilliseconds < 2000)
            {
                Thread.Sleep(10);
            }

            if (this.ConnectionStatus == EConnectionStatus.Connecting)
            {
                this.StopReceive();
                this.ConnectionStatus = EConnectionStatus.NotConnected;
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

                Message message = new Message(data, msgid, this.RemoteEndpoint, EMessageType.Data);
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
        protected override void Send(Message msg)
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

            if (DateTime.UtcNow.Subtract(this.Statistics.LastDataReceived).TotalSeconds > this.SocketSettings.TimeoutTime)
            {
                this.StopReceive();
                this.Disconnected?.Invoke(EDisconnectBy.ConnectionLost, "timeout");
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

            Message disc = new Message
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
                while (this.IncommingMessages.TryDequeue(out Message msg))
                {
                    if (this.ReceivedMessagesBuffer.Contains(msg.MessageId))
                    {
                        this.Logger?.LogInfo("Skipping already received message.");
                        continue;
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
            }
        }

        /// <summary>
        /// Called by receiver-task, asynchronous, dispatch internal messages
        /// </summary>
        /// <param name="msg"></param>
        protected override void ReceivedInternalMessage(Message msg)
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
            }
            else if (msg.MessageType == EMessageType.Disconnect && this.ConnectionStatus > EConnectionStatus.NotConnected && this.ConnectionStatus < EConnectionStatus.Disconnected)
            {
                //Server wants to disconnect client
                this.ConnectionStatus = EConnectionStatus.Disconnected;

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
                        this.Disconnected?.Invoke(EDisconnectBy.Server, reason);
                        this.StopReceive();
                    }
                });
            }
            else if (msg.MessageType == EMessageType.HeartBeat)
            {
                int ping = -1;
                if (msg.Data.Length >= 4)
                {
                    ping = BitConverter.ToInt32(msg.Data);
                }
                this.RTT = ping;
                this.Logger?.LogInfo("My ping is " + ping);
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
