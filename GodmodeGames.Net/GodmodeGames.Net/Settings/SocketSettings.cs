namespace GodmodeGames.Net.Settings
{
    public enum EUdpSendMode { Reliable, Unreliable };

    public abstract class SocketSettings
    {
        /// <summary>
        /// How long to wait for discover-response on connecting, in milliseconds
        /// </summary>
        public ushort ConnectingTimeout = 2000;
        /// <summary>
        /// Connection timeout after milliseconds
        /// </summary>
        public ushort TimeoutTime = 20000;
        /// <summary>
        /// Sleep-time for sending task, when queue is empty, in milliseconds
        /// </summary>
        public ushort SendTickrate = 10;
        /// <summary>
        /// Buffersize for incomming messages
        /// </summary>
        public int ReceiveBufferSize = 1024 * 1024;
        /// <summary>
        /// Buffersize for outgoing messages
        /// </summary>
        public int SendBufferSize = 1024 * 1024;
        /// <summary>
        /// Default send-mode (udp only) - reliable or unreliable
        /// </summary>
        public EUdpSendMode UdpDefaultSendMode = EUdpSendMode.Reliable;
        /// <summary>
        /// Tcp or Udp as transport layer
        /// </summary>
        public enum ETransport : byte { Udp, Tcp }
        /// <summary>
        /// what transport layer is used
        /// </summary>
        public ETransport Transport = ETransport.Udp;
        /// <summary>
        /// when to resend a message, if no ack has arrived
        /// </summary>
        public ushort UdpReliableResendTime = 500;
        /// <summary>
        /// how often will the message be resend, before giving it up
        /// </summary>
        public int UdpResendTries = 10;
        /// <summary>
        /// Buffer the last messages, to avoid dublicate receive
        /// </summary>
        public ushort UdpDublicateMessagesBuffer = 100;
    }
}
