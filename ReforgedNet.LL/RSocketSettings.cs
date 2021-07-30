namespace ReforgedNet.LL
{
    public class RSocketSettings
    {
        public int SendTickrateIsMs = 10;
        /// <summary>Delay in milliseconds to the next sending retry.</summary>
        public int SendRetryDelay = 100;
        /// <summary>How often should we retry sending a reliable message before throwing/logging an error. Time = NumberOfRetryTimes * SendRetryDelay</summary>
        public int NumberOfSendRetries = 10;
        /// <summary>timeout for pending disconnects by server</summary>
        public int PendingDisconnectTimeout = 1000;
        /// <summary>how many transaction-ids should be stored, preventing double send reliable messages on bad ping</summary>
        public int StoreLastMessages = 1000;
        /// <summary> should incoming messages be handled in main-thread, or instant? </summary>
        public bool HandleMessagesInMainThread = false;
        /// <summary> buffer-size for receiving </summary>
        public int BufferSize = 65536;
    }
}
