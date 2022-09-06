using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport;
using GodmodeGames.Net.Transport.Udp;
using System;
using System.Net;

namespace GodmodeGames.Net
{
    public class GGConnection : GGSocket
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
        /// Transport layer 
        /// </summary>
        internal IConnectionTransport Transport = null;

        internal GGConnection(IServerTransport servertransport, ServerSocketSettings settings, ILogger logger, IPEndPoint endpoint)
        {
            this.ServerTransport = servertransport;
            this.Settings = settings;
            this.Logger = logger;
            this.ClientEndpoint = endpoint;

            if (servertransport is UdpServerListener)
            {
                this.Transport = new UdpConnection(this);
            }
            else
            {
                throw new NotImplementedException("TODO TCP");
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
    }
}
