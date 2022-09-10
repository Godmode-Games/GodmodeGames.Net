using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using GodmodeGames.Net.Transport.Statistics;
using System.Collections.Concurrent;
using System.Net;

namespace GodmodeGames.Net.Transport
{
    public interface IServerTransport
    {
        enum EListeningStatus : byte { NotListening, Listening, ShuttingDown, Shutdown }
        EListeningStatus ListeningStatus { get; set; }

        /// <summary>
        /// Send- and Receive-statistics
        /// </summary>
        GGStatistics Statistics { get; set; }

        #region Events
        delegate void ClientConnectHandler(GGConnection connection);
        event ClientConnectHandler ClientConnected;
        delegate void ClientDisconnectHandler(GGConnection connection, string reason);
        event ClientDisconnectHandler ClientDisconnected;
        delegate void ReceivedMessageHandler(byte[] data, GGConnection connection);
        event ReceivedMessageHandler ReceivedData;
        delegate void ShutdownCompleteHandler();
        event ShutdownCompleteHandler ShutdownCompleted;
        #endregion

        /// <summary>
        /// Connections a the transport layer
        /// </summary>
        ConcurrentDictionary<IPEndPoint,GGConnection> Connections { get; set; }

        /// <summary>
        /// Initialize the socket
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="logger"></param>
        void Inititalize(ServerSocketSettings settings, ILogger logger);
        /// <summary>
        /// Start listening on a port
        /// </summary>
        /// <param name="endpoint"></param>
        void StartListening(IPEndPoint endpoint);
        /// <summary>
        /// have to be executed in update loop
        /// </summary>
        void Tick();
        /// <summary>
        /// Send data to a connection, reliability is set by server settings
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        void Send(byte[] data, GGConnection connection);
        /// <summary>
        /// Send data to a connection, reliable or not 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="connection"></param>
        /// <param name="reliable"></param>
        void Send(byte[] data, GGConnection connection, bool reliable = true);
        /// <summary>
        /// Disconnect a client connection
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="reason"></param>
        void DisconnectClient(GGConnection connection, string reason = null);
        /// <summary>
        /// Shutdown the server synchronous
        /// </summary>
        /// <returns></returns>
        bool Shutdown(string reason);
        /// <summary>
        /// Shutdown the server asynchronous
        /// </summary>
        void ShutdownAsync(string reason);
    }
}
