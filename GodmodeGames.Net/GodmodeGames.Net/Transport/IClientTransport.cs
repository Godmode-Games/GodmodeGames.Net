using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using System.Net;
using static GodmodeGames.Net.GGClient;

namespace GodmodeGames.Net.Transport
{
    internal interface IClientTransport
    {
        ClientStatistics Statistics { get; set; }
        
        public enum EConnectionStatus : byte { NotConnected, Connecting, Connected, Disconnecting, Disconnected }
        EConnectionStatus ConnectionStatus { get; set; }
        /// <summary>
        /// Round Trip Time (Ping)
        /// </summary>
        int RTT { get; set; }
        /// <summary>
        /// Initialize client transport layer
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        void Inititalize(ClientSocketSettings settings, ILogger logger);
        /// <summary>
        /// connect asynchronous to an server endpoint
        /// </summary>
        /// <param name="endpoint"></param>
        void ConnectAsync(IPEndPoint endpoint);
        /// <summary>
        /// connect synchronous to an server endpoint
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        bool Connect(IPEndPoint endpoint);
        /// <summary>
        /// disconnect synchronous with a reason
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        bool Disconnect(string reason = null);
        /// <summary>
        /// disconnect asynchronous with a reason
        /// </summary>
        /// <param name="reason"></param>
        void DisconnectAsync(string reason = null);
        /// <summary>
        /// Send data to the server, reliability is set by client settings
        /// </summary>
        /// <param name="data"></param>
        void Send(byte[] data);
        /// <summary>
        /// Send data to the server, reliable or not
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        void Send(byte[] data, bool reliable = true);
        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        void Tick();

        #region Events
        delegate void ReceiveDataHandler(byte[] data);
        event ReceiveDataHandler ReceivedData;
        delegate void ConnectAttemptHandler(bool success);
        event ConnectAttemptHandler ConnectAttempt;
        delegate void DisconnectedHandler(EDisconnectBy disconnect_by, string reason = null);
        event DisconnectedHandler Disconnected;
        #endregion
    }
}
