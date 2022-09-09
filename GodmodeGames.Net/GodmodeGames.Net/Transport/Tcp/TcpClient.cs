using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using static GodmodeGames.Net.Transport.IClientTransport;
using System.Net.Security;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System;
using System.Security.Authentication;
using System.Collections;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static GodmodeGames.Net.Transport.Tcp.TcpMessage;
using System.IO;
using GodmodeGames.Net.Utilities;

namespace GodmodeGames.Net.Transport.Tcp
{
    internal class TcpClient : IClientTransport
    {
        public ClientStatistics Statistics { get; set; } = new ClientStatistics();
        public EConnectionStatus ConnectionStatus { get; set; } = EConnectionStatus.NotConnected;
        public int RTT { get; set; } = -1;

        #region Events
        public event ReceiveDataHandler ReceivedData;
        public event ConnectAttemptHandler ConnectAttempt;
        public event DisconnectedHandler Disconnected;
        #endregion

        private System.Net.Sockets.TcpClient Socket = null;
        private CancellationTokenSource CTS = null;
        private NetworkStream myStream = null;
        private SslStream mySSLStream = null;

        private ILogger Logger = null;
        private SocketSettings SocketSettings = null;
        private IPEndPoint RemoteEndpoint = null;

        private byte[] asyncBuff;
        private ConcurrentQueue<TcpMessage> OutgoingMessages = new ConcurrentQueue<TcpMessage>();
        private ConcurrentQueue<TcpMessage> IncommingMessages = new ConcurrentQueue<TcpMessage>();
        private ConcurrentQueue<TickEvent> TickEvents = new ConcurrentQueue<TickEvent>();//Event that should be invoked in Tick-method
        private Task SendTask = null;

        public void Inititalize(ClientSocketSettings settings, ILogger logger)
        {
            this.SocketSettings = settings;
            this.Logger = logger;
            this.CTS = new CancellationTokenSource();
            this.ConnectionStatus = EConnectionStatus.NotConnected;
        }

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
                this.StopReceive();
                this.ConnectionStatus = EConnectionStatus.NotConnected;
            }

            return this.ConnectionStatus == EConnectionStatus.Connected;
        }

        public void ConnectAsync(IPEndPoint endpoint)
        {
            if (endpoint == null)
            {
                this.Logger?.LogError("No server endpoint defined!");
                return;
            }

            this.RemoteEndpoint = endpoint;

            this.StopReceive();

            this.ConnectionStatus = EConnectionStatus.Connecting;

            this.Socket = new System.Net.Sockets.TcpClient();
            this.Socket.ReceiveBufferSize = this.SocketSettings.ReceiveBufferSize;
            this.Socket.SendBufferSize = this.SocketSettings.SendBufferSize; ;
            this.Socket.NoDelay = true;

            Array.Resize(ref this.asyncBuff, this.SocketSettings.ReceiveBufferSize);
            try
            {
                this.Socket.BeginConnect(this.RemoteEndpoint.Address, this.RemoteEndpoint.Port, new AsyncCallback(this.ConnectCallback), this.Socket);
            }
            catch (Exception e)
            {
                this.Logger?.LogError("Error while connecting to " + this.RemoteEndpoint.ToString() + ": " + e.Message);
                this.TickEvents.Enqueue(new TickEvent()
                {
                    OnTick = () =>
                    {
                        this.ConnectAttempt?.Invoke(false);
                    }
                });
                return;
            }
        }

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

        public void DisconnectAsync(string reason = null)
        {
            if (this.ConnectionStatus >= EConnectionStatus.Disconnecting)
            {
                return;
            }

            this.ConnectionStatus = EConnectionStatus.Disconnecting;

            byte[] data = new byte[0];
            if (reason != null)
            {
                data = Encoding.UTF8.GetBytes(reason);
            }

            TcpMessage disc = new TcpMessage
            {
                MessageType = EMessageType.Disconnect,
                Data = data,
                Client = null
            };

            this.InternalSendTo(disc);

            this.StopReceive();

            this.ConnectionStatus = EConnectionStatus.Disconnected;
            this.TickEvents.Enqueue(new TickEvent()
            {
                OnTick = () =>
                {
                    this.Disconnected?.Invoke(GGClient.EDisconnectBy.Client, reason);
                }
            });
        }

        public void Send(byte[] data)
        {
            TcpMessage msg = new TcpMessage(data, null, TcpMessage.EMessageType.Data);
            this.OutgoingMessages.Enqueue(msg);
        }

        public void Send(byte[] data, bool reliable = true)
        {
            this.Send(data);
        }

        public void Tick()
        {
            if (this.IncommingMessages.Count > 0)
            {
                while (this.IncommingMessages.TryDequeue(out TcpMessage msg))
                {
                    this.ReceivedData?.Invoke(msg.Data);
                }
            }

            while (this.TickEvents.TryDequeue(out TickEvent tick))
            {
                tick.OnTick?.Invoke();
            }
        }

        private void StopReceive()
        {
            if (this.Socket != null)
            {
                this.OutgoingMessages.Clear();
                this.IncommingMessages.Clear();

                try
                {
                    this.CTS.Cancel();
                    this.SendTask?.Wait(500);
                    this.Socket?.Close();
                    this.Socket = null;
                }
                catch (Exception)
                {

                }
            }

            this.ConnectionStatus = EConnectionStatus.Disconnected;
        }

        private void ConnectCallback(IAsyncResult result)
        {
            if (this.Socket != null)
            {
                try
                {
                    this.Socket.EndConnect(result);

                    if (this.Socket.Connected == false)
                    {
                        this.TickEvents.Enqueue(new TickEvent()
                        {
                            OnTick = () =>
                            {
                                this.ConnectAttempt?.Invoke(false);
                            }
                        });
                        return;
                    }
                    else
                    {
                        if (this.SocketSettings.TcpSSL == false)
                        {
                            this.myStream = this.Socket.GetStream();
                            this.myStream.BeginRead(this.asyncBuff, 0, this.SocketSettings.ReceiveBufferSize, new AsyncCallback(this.OnReceive), null);
                        }
                        else
                        {
                            try
                            {
                                this.mySSLStream = new SslStream(this.Socket.GetStream(), false, new RemoteCertificateValidationCallback(this.ValidateServerCertificate), null);
                                this.mySSLStream.AuthenticateAsClient(this.RemoteEndpoint.Address.ToString());
                            }
                            catch (AuthenticationException e)
                            {
                                this.Logger?.LogError("ssl-authentication failed - closing the connection. " + e.Message);
                                this.StopReceive();
                                this.ConnectionStatus = EConnectionStatus.NotConnected;
                                this.TickEvents.Enqueue(new TickEvent()
                                {
                                    OnTick = () =>
                                    {
                                        this.ConnectAttempt?.Invoke(false);
                                    }
                                });
                                return;
                            }
                            catch (Exception e)
                            {
                                this.Logger?.LogError("ssl-authentication exception - closing the connection. " + e.Message);
                                this.StopReceive();
                                this.ConnectionStatus = EConnectionStatus.NotConnected;
                                this.TickEvents.Enqueue(new TickEvent()
                                {
                                    OnTick = () =>
                                    {
                                        this.ConnectAttempt?.Invoke(false);
                                    }
                                });
                                return;
                            }
                            this.mySSLStream.BeginRead(this.asyncBuff, 0, this.SocketSettings.ReceiveBufferSize, new AsyncCallback(this.OnReceive), null);
                        }

                        this.StartSendingTask();

                        this.Statistics.LastDataReceived = DateTime.UtcNow;
                        this.ConnectionStatus = EConnectionStatus.Connected;

                        this.TickEvents.Enqueue(new TickEvent()
                        {
                            OnTick = () =>
                            {
                                this.ConnectAttempt?.Invoke(true);
                            }
                        });
                    }
                }
                catch (SocketException socketexcepion)
                {
                    //10061 = Connection refused
                    if (socketexcepion.ErrorCode != 10061)
                    {
                        this.Logger?.LogError("Failed to connect to server " + this.RemoteEndpoint.ToString() + ": " + socketexcepion.Message);
                    }
                    this.ConnectionStatus = EConnectionStatus.NotConnected;
                    this.StopReceive();
                    this.TickEvents.Enqueue(new TickEvent()
                    {
                        OnTick = () =>
                        {
                            this.ConnectAttempt?.Invoke(false);
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Logger?.LogError("Failed to connect to server " + this.RemoteEndpoint.ToString() + ": " + ex.Message);
                    this.ConnectionStatus = EConnectionStatus.NotConnected;
                    this.StopReceive();
                    this.TickEvents.Enqueue(new TickEvent()
                    {
                        OnTick = () =>
                        {
                            this.ConnectAttempt?.Invoke(false);
                        }
                    });
                }
            }
        }

        private void OnReceive(IAsyncResult result)
        {
            try
            {
                if (this.Socket == null)
                {
                    //Server disconnected
                    return;
                }

                this.Statistics.LastDataReceived = DateTime.Now;

                int byteArray = 0;
                if (this.SocketSettings.TcpSSL == false)
                {
                    byteArray = this.myStream.EndRead(result);
                }
                else
                {
                    byteArray = this.mySSLStream.EndRead(result);
                }

                if (byteArray == 0)
                {
                    this.StopReceive();
                    return;
                }

                byte[] myBytes = new byte[byteArray];
                System.Buffer.BlockCopy(this.asyncBuff, 0, myBytes, 0, byteArray);
                this.asyncBuff = new byte[this.SocketSettings.ReceiveBufferSize];

                TcpMessage msg = new TcpMessage();
                msg.Deserialize(myBytes, null);

                if (msg.MessageType == EMessageType.Data)
                {
                    this.IncommingMessages.Enqueue(msg);
                }
                else
                {
                    this.ReceivedInternalMessage(msg);
                }

                this.Statistics.UpdateReceiveStatistics(byteArray);

                if (this.Socket == null)
                {
                    return;
                }

                if (this.SocketSettings.TcpSSL == false)
                {
                    this.myStream.BeginRead(this.asyncBuff, 0, 8192, new AsyncCallback(this.OnReceive), null);
                }
                else
                {
                    this.mySSLStream.BeginRead(this.asyncBuff, 0, 8192, new AsyncCallback(this.OnReceive), null);
                }

                //ggf. Send-Task neu Starten
                this.StartSendingTask();
                if (this.Socket == null || (this.SocketSettings.TcpSSL == false && this.myStream.CanRead == false) || (this.SocketSettings.TcpSSL == true && this.mySSLStream.CanRead == false))
                {
                    this.TickEvents.Enqueue(new TickEvent()
                    {
                        OnTick = () =>
                        {
                            this.Disconnected?.Invoke(GGClient.EDisconnectBy.Client);
                        }
                    });
                    this.StopReceive();
                }
            }
            catch (IOException e)
            {
                this.TickEvents.Enqueue(new TickEvent()
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(GGClient.EDisconnectBy.Server, "closed connection");
                    }
                });
                this.StopReceive();
            }
            catch (Exception e)
            {
                this.Logger?.LogError("OnReceive Error: " + e.Message);
                this.TickEvents.Enqueue(new TickEvent()
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(GGClient.EDisconnectBy.Client);
                    }
                });
                this.StopReceive();
            }
        }

        private void StartSendingTask()
        {
            if (this.SendTask != null && this.SendTask.Status != TaskStatus.Running)
            {
                this.CTS?.Cancel();
                this.SendTask.Wait(500);
                this.SendTask = null;
            }

            this.CTS = new CancellationTokenSource();

            if (this.SendTask == null)
            {
                this.SendTask = Task.Factory.StartNew(() =>
                {
                    while (!this.CTS.IsCancellationRequested)
                    {
                        while (this.OutgoingMessages.Count > 0 && this.OutgoingMessages.TryDequeue(out TcpMessage msg))
                        {
                            this.InternalSendTo(msg);
                        }
                        Task.Delay(10);
                    }
                });
            }
        }

        private void InternalSendTo(TcpMessage msg)
        {
            byte[] data = msg.Serialize();
            if (this.SocketSettings.TcpSSL == false)
            {
                if (this.myStream != null)
                {
                    this.myStream.Write(data, 0, data.Length);
                    this.myStream.Flush();
                }
            }
            else
            {
                if (this.mySSLStream != null)
                {
                    this.mySSLStream.Write(data, 0, data.Length);
                    this.mySSLStream.Flush();
                }
            }

            this.Statistics.UpdateSentStatistics(data.Length);
        }

        private void ReceivedInternalMessage(TcpMessage msg)
        {
            if (msg.MessageType == EMessageType.Disconnect)
            {
                string reason = Helper.BytesToString(msg.Data);
                this.TickEvents.Enqueue(new TickEvent()
                {
                    OnTick = () =>
                    {
                        this.Disconnected?.Invoke(GGClient.EDisconnectBy.Server, reason);
                    }
                });
                this.StopReceive();
            }
            else if (msg.MessageType == EMessageType.HeartBeatPing)
            {
                if (msg.Data.Length >= 4)
                {
                    //first 4 bytes are the last ping
                    this.RTT = BitConverter.ToInt32(msg.Data, 3);
                }
                TcpMessage pong = new TcpMessage
                {
                    Data = msg.Data,
                    Client = null,
                    MessageType = EMessageType.HeartbeatPong
                };
                this.InternalSendTo(pong);
            }
        }

        #region SSL-Validation
        private static Hashtable certificateErrors = new Hashtable();
        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            const SslPolicyErrors ignoredErrors = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;

            if ((sslPolicyErrors & ~ignoredErrors) == SslPolicyErrors.None)
            {
                return true;
            }

            this.Logger?.LogError("ssl certificate error: " + sslPolicyErrors.ToString());
            return false;
        }
        #endregion
    }
}
