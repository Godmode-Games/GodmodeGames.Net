namespace GodmodeGames.Net.Settings
{
    public class ServerSocketSettings : SocketSettings
    {
        /// <summary>
        /// after how many milliseconds will a client be disconnected, when disconnect is pending
        /// </summary>
        public int UdpPendingDisconnectsTimeout = 5000;
    }
}
