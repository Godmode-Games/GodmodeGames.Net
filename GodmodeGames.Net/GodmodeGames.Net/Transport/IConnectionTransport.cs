namespace GodmodeGames.Net.Transport
{
    internal interface IConnectionTransport
    { 
        GGConnection Connection { get; set; }
        /// <summary>
        /// Send Data to a client, reliability is set by server settings
        /// </summary>
        /// <param name="data"></param>
        void Send(byte[] data);
        /// <summary>
        /// Send Data to a client, reliable or not 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="reliable"></param>
        void Send(byte[] data, bool reliable = true);
        /// <summary>
        /// disconnect the client
        /// </summary>
        /// <param name="reason"></param>
        void Disconnect(string reason = null);
    }
}
