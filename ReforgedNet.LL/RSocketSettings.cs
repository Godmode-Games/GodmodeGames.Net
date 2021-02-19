using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    public class RSocketSettings
    {
        public int SendTickrateIsMs = 10;
        /// <summary>Delay in milliseconds to the next sending retry.</summary>
        public int SendRetryDelay = 100;
        /// <summary>How often should we retry sending a reliable message before throwing/logging an error. Time = NumberOfRetryTimes * SendRetryDelay</summary>
        public int NumberOfSendRetries = 10;
        /// <summary>timeout for pending disconnects by server
        public int PendingDisconnectTimeout = 1000;
    }
}
