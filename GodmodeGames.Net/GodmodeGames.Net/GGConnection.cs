using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport;
using GodmodeGames.Net.Transport.Statistics;
using GodmodeGames.Net.Transport.Tcp;
using GodmodeGames.Net.Transport.Udp;
using System;
using System.Diagnostics;
using System.Net;

namespace GodmodeGames.Net
{
    public class GGConnection : GGPeer
    {
        /// <summary>
        /// Transport layer of the server, the connection is part of
        /// </summary>
        internal IServerTransport ServerTransport = null;
        /// <summary>
        /// Server settings, the connection is part of
        /// </summary>
        public ServerSocketSettings Settings { get; private set; }
        /// <summary>
        /// Endpoint of the connetion
        /// </summary>
        public IPEndPoint ClientEndpoint = null;
        /// <summary>
        /// Statistics of the connection
        /// </summary>
        public GGStatistics Statistics = new GGStatistics();
        /// <summary>
        /// Transport layer 
        /// </summary>
        internal IConnectionTransport Transport = null;
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
        /// Current Ping of the client
        /// </summary>
        public int Ping = -1;

        public GGConnection(IServerTransport servertransport, ServerSocketSettings settings, ILogger logger, IPEndPoint endpoint)
        {
            this.ServerTransport = servertransport;
            this.Settings = settings;
            this.Logger = logger;
            this.ClientEndpoint = endpoint;
            this.LastHeartbeat = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(this.Settings.HeartbeatInterval - 1000));//first ping in 1 seconds after connect, then every HeartbeatInterval
            this.HeartbeatStopwatch = new Stopwatch();

            if (servertransport is UdpServerListener)
            {
                this.Transport = new UdpConnection(this);
            }
            else
            {
                this.Transport = new TcpConnection(this);
            }
        }

        /// <summary>
        /// send data to the connection
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            this.ServerTransport.Send(data, this);
        }

        /// <summary>
        /// send data to the connection, reliable or not
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, bool reliable)
        {
            this.ServerTransport.Send(data, this, reliable);
        }

        internal void StartHeartbeat(int messageid)
        {
            this.LastHeartbeatId = messageid;
            this.LastHeartbeat = DateTime.UtcNow;
            this.HeartbeatStopwatch.Restart();
        }

        internal void HeartbeatResponseReceived(int id)
        {
            if (this.LastHeartbeatId == id)
            {
                this.HeartbeatStopwatch.Stop();
                this.Ping = (int)this.HeartbeatStopwatch.ElapsedMilliseconds;

                //reset
                this.LastHeartbeatId = -1;
                this.LastHeartbeat = DateTime.UtcNow;
            }
        }

        public override string ToString()
        {
            return this.ClientEndpoint.ToString();
        }
    }
}
