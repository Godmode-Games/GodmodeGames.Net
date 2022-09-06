using System.Collections.Generic;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class UdpConnection : IConnectionTransport
    {
        public List<int> ReceivedMessagesBuffer = new List<int>();
        public GGConnection Connection { get; set; }

        public UdpConnection(GGConnection connection)
        {
            this.Connection = connection;
        }

        /// <summary>
        /// Send data to the connection, reliability set by server-settings
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            this.Connection?.Transport?.Send(data);
        }
        /// <summary>
        /// Send data to server, reliable or not
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        public void Send(byte[] data, bool reliable)
        {
            this.Connection?.Transport?.Send(data, reliable);
        }
        /// <summary>
        /// Disconnect the client with a reason
        /// </summary>
        /// <param name="reason"></param>
        public void Disconnect(string reason = null)
        {
            this.Connection?.ServerTransport?.DisconnectClient(this.Connection, reason);
        }
    }
}
