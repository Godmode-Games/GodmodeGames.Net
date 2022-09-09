using System;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class AckMessage : UdpMessage
    {
        /// <summary>
        /// when was the last send attempt
        /// </summary>
        public DateTime LastTryTime;
        /// <summary>
        /// how many times was the message send
        /// </summary>
        public int RetryTimes = 0;

        public AckMessage()
        {

        }

        public AckMessage(UdpMessage msg) : base(msg.Data, msg.MessageId, msg.RemoteEndpoint, msg.MessageType)
        {
            this.LastTryTime = DateTime.UtcNow;
            this.RetryTimes = 0;
        }
    }
}
