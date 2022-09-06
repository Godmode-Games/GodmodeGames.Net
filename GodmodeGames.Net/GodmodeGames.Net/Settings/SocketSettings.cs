namespace GodmodeGames.Net.Settings
{
    public enum EUdpSendMode { Reliable, Unreliable };

    public abstract class SocketSettings
    {
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
        /// Simulate packet lost while sending packets (in percent 0 - 100)
        /// </summary>
        public int SimulatedPacketLostSend = 0;
        /// <summary>
        /// Simulate packet lost while receiving packets (in percent 0 - 100)
        /// </summary>
        public int SimulatedPacketLostReceive = 0;
        /// <summary>
        /// Simulate ping on sending packets (in milliseconds)
        /// </summary>
        public int SimulatedPing = 0;
        /// <summary>
        /// Tcp or Udp as transport layer
        /// </summary>
        public enum ETransport : byte { Udp, Tcp }
        /// <summary>
        /// what transport layer is used
        /// </summary>
        public ETransport Transport = ETransport.Udp;
        /// <summary>
        /// Default send-mode (udp only) - reliable or unreliable
        /// </summary>
        public EUdpSendMode UdpDefaultSendMode = EUdpSendMode.Reliable;
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
