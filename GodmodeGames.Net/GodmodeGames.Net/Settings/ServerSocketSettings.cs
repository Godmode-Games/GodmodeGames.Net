﻿namespace GodmodeGames.Net.Settings
{
    public class ServerSocketSettings : SocketSettings
    {
        /// <summary>
        /// after how many milliseconds will a client be disconnected, when disconnect is pending
        /// </summary>
        public int UdpPendingDisconnectsTimeout = 5000;

        /// <summary>
        /// Server ssl certificate
        /// </summary>
        public string TcpSSLCert = null;
        /// <summary>
        /// Password for ssl certificate
        /// </summary>
        public string TcpSSLCertPassword = null;
    }
}