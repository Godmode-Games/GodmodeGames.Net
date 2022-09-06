using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using static GodmodeGames.Net.Transport.Message;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Logging;
using System.Collections.Concurrent;
using System.Net;

namespace GodmodeGames.Net.Transport.Udp
{
    internal abstract class UdpPeer
    {
        protected Socket Socket = null;
        protected CancellationTokenSource CTS = null;
        protected Task ReceiveTask = null;
        protected Task SendTask = null;

        protected ILogger Logger;
        internal SocketSettings SocketSettings;
        protected IPEndPoint RemoteEndpoint;

        /// <summary>
        /// Incomming messages from server 
        /// </summary>
        protected ConcurrentQueue<Message> IncommingMessages = new ConcurrentQueue<Message>();
        /// <summary>
        /// Outgoing messages to server
        /// </summary>
        protected ConcurrentQueue<Message> OutgoingMessages = new ConcurrentQueue<Message>();
        /// <summary>
        /// Outgoing messages waiting for acknowledgment
        /// </summary>
        protected ConcurrentDictionary<int, AckMessage> PendingUnacknowledgedMessages = new ConcurrentDictionary<int, AckMessage>();
        /// <summary>
        /// reliablie ID counter
        /// </summary>
        protected int NextReliableMessageId = 1;

        /// <summary>
        /// Inititalize the socket
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        internal void Initialize(SocketSettings settings, ILogger logger)
        {
            this.SocketSettings = settings;
            this.Logger = logger;

            this.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                SendBufferSize = this.SocketSettings.SendBufferSize,
                ReceiveBufferSize = this.SocketSettings.ReceiveBufferSize
            };

            this.CTS = new CancellationTokenSource();
        }

        /// <summary>
        /// add a message to the outgoing queue
        /// </summary>
        /// <param name="msg"></param>
        protected virtual void Send(Message msg)
        {
            this.StartSendingTask();
            this.OutgoingMessages.Enqueue(msg);
        }

        protected abstract void UpdateStatisticsReceive(int bytes, IPEndPoint endpoint);
        protected abstract void UpdateStatisticsSent(int bytes, IPEndPoint endpoint);
        protected abstract void UpdatePacketLost(IPEndPoint endpoint);
        protected abstract void PacketLost(AckMessage message);
        protected abstract void ConnectionFailed(IPEndPoint endpoint);
        protected abstract void ReceivedInternalMessage(Message msg);
        protected abstract void ReceivedInternalACK(AckMessage msg);

        /// <summary>
        /// start the receiving task, if not running
        /// </summary>
        protected void StartReceivingTask()
        {
            if (this.Socket == null)
            {
                return;
            }
            if (this.ReceiveTask == null || this.ReceiveTask.IsCompleted == true)
            {
                this.ReceiveTask = Task.Factory.StartNew(() =>
                {
                    while (!this.CTS.IsCancellationRequested)
                    {
                        byte[] data = new byte[this.SocketSettings.ReceiveBufferSize];
                        EndPoint endPoint = this.RemoteEndpoint;

                        int numOfReceivedBytes = 0;
                        try
                        {
                            numOfReceivedBytes = this.Socket!.ReceiveFrom(data, 0, this.SocketSettings.ReceiveBufferSize, SocketFlags.None, ref endPoint);
                        }
                        catch (SocketException ex)
                        {
                            if (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.NotSocket ||ex.SocketErrorCode == SocketError.ConnectionReset)
                            {
                                this.ConnectionFailed((IPEndPoint)endPoint);
                                continue;
                            }
                            this.Logger?.LogError("Error while receiving data: (" + ex.ErrorCode + ") " + ex.Message);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            this.Logger?.LogError("Error while receiving data: " + ex.Message);
                            continue;
                        }

                        if (numOfReceivedBytes > 0)
                        {
                            this.UpdateStatisticsReceive(numOfReceivedBytes, (IPEndPoint)endPoint);

                            if (this.SocketSettings.SimulatedPacketLostReceive > 0)
                            {
                                //simulate packet lost
                                int percent = new Random().Next(0, 101);
                                int packetlost = Math.Clamp(this.SocketSettings.SimulatedPacketLostReceive, 0, 100);
                                if (percent < packetlost)
                                {
                                    this.Logger?.LogWarning("Simulate receive Packetlost...");
                                    continue;
                                }
                            }

                            Message msg = new Message();
                            if (msg.Deserialize(data.Take(numOfReceivedBytes).ToArray(), (IPEndPoint)endPoint))
                            {
                                if (msg.MessageType == EMessageType.Ack)
                                {
                                    if (this.PendingUnacknowledgedMessages.TryRemove(msg.MessageId, out AckMessage ack))
                                    {
                                        this.ReceivedInternalACK(ack);
                                    }
                                }
                                else
                                {
                                    if (msg.MessageId >= 0)
                                    {
                                        //send ack for reliable message back...
                                        Message ack = new Message
                                        {
                                            MessageType = EMessageType.Ack,
                                            MessageId = msg.MessageId,
                                            RemoteEndpoint = msg.RemoteEndpoint
                                        };
                                        this.InternalSendTo(ack);
                                    }

                                    if (msg.MessageType != EMessageType.Data)
                                    {
                                        this.ReceivedInternalMessage(msg);
                                    }
                                    else
                                    {
                                        this.IncommingMessages.Enqueue(msg);
                                    }                                    
                                }                                
                            }
                            else
                            {
                                this.Logger?.LogError("Invalid message received from " + endPoint.ToString() + "!");
                            }
                        }
                    }
                });
                this.ReceiveTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// start the sending task, if not running
        /// </summary>
        protected void StartSendingTask()
        {
            if (this.Socket == null)
            {
                return;
            }

            if (this.SendTask == null || this.SendTask.IsCompleted == true)
            {
                this.SendTask = Task.Factory.StartNew(() =>
                {
                    while (!this.OutgoingMessages.IsEmpty || !this.PendingUnacknowledgedMessages.IsEmpty || !this.CTS.IsCancellationRequested)
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();

                        //Send Messages...
                        if (this.Socket != null && !this.OutgoingMessages.IsEmpty)
                        {
                            while (this.OutgoingMessages.TryDequeue(out Message msg))
                            {
                                this.InternalSendTo(msg);
                            }
                        }

                        //resend unacknowledged Messages 
                        if (this.Socket != null && !this.PendingUnacknowledgedMessages.IsEmpty && !this.CTS.IsCancellationRequested)
                        {
                            foreach (KeyValuePair<int, AckMessage> kvp in this.PendingUnacknowledgedMessages)
                            {
                                if (kvp.Value.LastTryTime.AddMilliseconds(this.SocketSettings.UdpReliableResendTime) < DateTime.UtcNow)
                                {
                                    this.UpdatePacketLost(kvp.Value.RemoteEndpoint);

                                    if (kvp.Value.RetryTimes > this.SocketSettings.UdpResendTries)
                                    {
                                        this.PendingUnacknowledgedMessages.TryRemove(kvp.Key, out _);
                                        this.Logger?.LogWarning("Number of max retries for packets resend reached! " + kvp.Value.MessageType);
                                        this.PacketLost(kvp.Value);
                                    }
                                    else
                                    {
                                        this.InternalSendTo(kvp.Value);
                                        kvp.Value.RetryTimes++;
                                        kvp.Value.LastTryTime = DateTime.UtcNow;
                                    }
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

        protected void InternalSendTo(Message msg)
        {
            if (this.SocketSettings.SimulatedPacketLostSend > 0)
            {
                //simulate packet lost
                int percent = new Random().Next(0, 101);
                int packetlost = Math.Clamp(this.SocketSettings.SimulatedPacketLostSend, 0, 100);
                if (percent < packetlost)
                {
                    this.Logger?.LogWarning("Simulate send packet-lost...");

                    if (msg.MessageType != EMessageType.Ack && msg.MessageId >= 0)
                    {
                        AckMessage ack = new AckMessage(msg);
                        this.PendingUnacknowledgedMessages.TryAdd(msg.MessageId, ack);
                    }

                    return;
                }
            }

            if (this.SocketSettings.SimulatedPing > 0)
            {
                //Simulate ping, re-queue the packet until it can be send
                if (msg.AddedSimulatedPing == false)
                {
                    msg.ProcessTime = DateTime.UtcNow.AddMilliseconds(this.SocketSettings.SimulatedPing);
                    msg.AddedSimulatedPing = true;
                    this.OutgoingMessages.Enqueue(msg);
                    return;
                }
                else
                {
                    if (msg.ProcessTime > DateTime.UtcNow)
                    {
                        this.OutgoingMessages.Enqueue(msg);
                        return;
                    }
                }
            }

            byte[] data;

            try
            {
                data = msg.Serialize();
            }
            catch (Exception ex)
            {
                this.Logger?.LogError("Error while serializing message: " + ex.Message);
                return;
            }

            int numOfSentBytes;
            try
            {
                numOfSentBytes = this.Socket.SendTo(data, 0, data.Length, SocketFlags.None, msg.RemoteEndpoint);
            }
            catch (Exception ex)
            {
                this.Logger?.LogError("Error while sending data to " + msg.RemoteEndpoint.ToString() + ": " + ex.Message);
                return;
            }

            if (numOfSentBytes == 0)
            {
                this.Logger?.LogWarning("Sent empty message.");
            }
            else
            {
                //update statistics...
                this.UpdateStatisticsSent(numOfSentBytes, msg.RemoteEndpoint);
                this.StartReceivingTask();
            }

            //check if it's a reliable message, add to pending-messages
            if (msg.MessageType != EMessageType.Ack && msg.MessageId >= 0)
            {
                AckMessage ack = new AckMessage(msg);
                this.PendingUnacknowledgedMessages.TryAdd(msg.MessageId, ack);
            }
        }

        /// <summary>
        /// get the next reliable id
        /// </summary>
        /// <returns></returns>
        protected int GetNextReliableId()
        {
            int id = this.NextReliableMessageId++;
            if (this.NextReliableMessageId > int.MaxValue)
            {
                this.NextReliableMessageId = 1;
            }

            return id;
        }
    }
}
