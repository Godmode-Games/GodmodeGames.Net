using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

//https://github.com/Die-SpieleBauer/reforged/blob/master/Reforged/NetworkingProjects/LoginServer/Network/SSLClient.cs
namespace GodmodeGames.Net.Transport.Tcp
{
    internal class TcpConnection : IConnectionTransport
    {
        public GGConnection Connection { get; set; }

        private System.Net.Sockets.TcpClient Socket = null;
        private SslStream mySSLStream = null;
        private NetworkStream myStream = null;
        private TcpServerListener Server = null;

        private byte[] readBuff;
        internal bool UseSSL => this.Connection != null ? this.Connection.Settings.TcpSSL : false;

        public TcpConnection(GGConnection connection)
        {
            this.Connection = connection;
        }

        public bool Initialize(System.Net.Sockets.TcpClient client, TcpServerListener server)
        {
            this.Socket = client;
            this.Server = server;

            this.Socket.ReceiveBufferSize = this.Connection.Settings.ReceiveBufferSize;
            this.Socket.SendBufferSize = this.Connection.Settings.SendBufferSize;

            Array.Resize(ref this.readBuff, this.Socket.ReceiveBufferSize);
            if (this.UseSSL == true)
            {
                this.mySSLStream = new SslStream(this.Socket.GetStream(), false);

                try
                {
                    this.mySSLStream.AuthenticateAsServer(this.Server.ServerCertificate, clientCertificateRequired: false, checkCertificateRevocation: true);
                    this.mySSLStream.BeginRead(this.readBuff, 0, this.Socket.ReceiveBufferSize, this.OnReceiveData, null);
                }
                catch (AuthenticationException e)
                {
                    string error = "Ssl authentication failed for " + this.Connection.ToString() + ": " + e.Message;

                    if (e.InnerException != null)
                    {
                        error += Environment.NewLine + "Inner exception: " + e.InnerException.Message;
                    }
                    this.Server?.Logger?.LogError(error);

                    this.mySSLStream.Close();
                    client.Close();
                    return false;
                }
                catch (Exception e)
                {
                    this.Server?.Logger?.LogError("Error while BeginRead for " + this.Connection.ToString() + ": " + e.Message);
                    this.mySSLStream.Close();
                    client.Close();
                    return false;
                }
            }
            else
            {
                this.myStream = this.Socket.GetStream();
                this.myStream.BeginRead(this.readBuff, 0, this.Socket.ReceiveBufferSize, this.OnReceiveData, null);
            }

            return true;
        }

        public void Disconnect(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                TcpMessage disc = new TcpMessage
                {
                    MessageType = TcpMessage.EMessageType.Disconnect,
                    Data = Encoding.UTF8.GetBytes(reason),
                    Client = null
                };

                byte[] data = disc.Serialize();
                this.SendToClient(data);
            }

            this.Server?.RemoveClient(this.Connection, reason);

            this.StopReceive();
        }

        public void Send(byte[] data)
        {
            this.Server?.Send(data, this.Connection);
        }

        public void Send(byte[] data, bool reliable = true)
        {
            this.Send(data);
        }

        internal void SendToClient(byte[] data)
        {
            if (this.Socket == null)
            {
                return;
            }
            if (this.UseSSL == true)
            {
                this.mySSLStream.Write(data);
                this.mySSLStream.Flush();
            }
            else
            {
                this.myStream.Write(data);
                this.myStream.Flush();
            }
        }

        private void StopReceive()
        {
            if (this.Socket != null)
            {
                this.Socket.Close();
                this.Socket = null;
            }

            if (this.myStream != null)
            {
                this.myStream.Close();
                this.myStream = null;
            }

            if (this.mySSLStream != null)
            {
                this.mySSLStream.Close();
                this.mySSLStream = null;
            }
        }

        private void OnReceiveData(IAsyncResult ar)
        {
            try
            {
                if (this.Socket == null)
                {
                    this.Disconnect();
                    return;
                }

                int readbytes;
                if (this.Connection.Settings.TcpSSL == true)
                {
                    readbytes = this.mySSLStream.EndRead(ar);
                }
                else
                {
                     readbytes = this.myStream.EndRead(ar);
                }
                
                if (readbytes <= 0)
                {
                    this.Disconnect();
                    return;
                }

                byte[] newbytes = null;
                Array.Resize(ref newbytes, readbytes);
                Buffer.BlockCopy(this.readBuff, 0, newbytes, 0, readbytes);

                this.Server.ClientDataReceived(newbytes, this.Connection);

                if (this.Socket == null)
                {
                    return;
                }

                if (this.UseSSL == true)
                {
                    this.mySSLStream.BeginRead(this.readBuff, 0, this.Socket.ReceiveBufferSize, this.OnReceiveData, null);
                }
                else
                {
                    this.myStream.BeginRead(this.readBuff, 0, this.Socket.ReceiveBufferSize, this.OnReceiveData, null);
                }
            }
            catch (AuthenticationException)
            {
                this.Server?.Logger?.LogError("ssl authentication failed - closing the connection to client " + this.Connection.ToString());
                this.Disconnect();
            }
            catch (Exception ex)
            {
                this.Server?.Logger?.LogError("error while receiving data from " + this.Connection.ToString() + ": " + ex.Message);
                this.Disconnect();
            }
        }
    }
}
