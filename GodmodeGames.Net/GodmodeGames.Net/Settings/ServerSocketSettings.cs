namespace GodmodeGames.Net.Settings
{
    public class ServerSocketSettings : SocketSettings
    {
        /// <summary>
        /// Heartbeat interval in milliseconds
        /// </summary>
        public int HeartbeatInterval = 5000;
        /// <summary>
        /// after how many milliseconds will a client be disconnected, when disconnect is pending
        /// </summary>
        public int UdpPendingDisconnectsTimeout = 5000;
    }
}
