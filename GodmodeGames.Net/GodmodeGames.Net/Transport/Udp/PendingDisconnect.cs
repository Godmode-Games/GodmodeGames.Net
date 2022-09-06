using System;

namespace GodmodeGames.Net.Transport.Udp
{
    public class PendingDisconnect
    {
        /// <summary>
        /// when was the disconnect request send
        /// </summary>
        public DateTime SendRequest = DateTime.UtcNow;
        /// <summary>
        /// Reason of disconnect
        /// </summary>
        public string Reason = null;
    }
}
