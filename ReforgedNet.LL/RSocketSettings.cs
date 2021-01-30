using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    public class RSocketSettings
    {
        public int SendTickrate
        {
            get => 1000 * SendTickrate;
            set => SendTickrateInMs = 1000 / value;
        }
        public int SendTickrateInMs = 1000 / 50;
        /// <summary>Delay in milliseconds to the next sending rety.</summary>
        public int SendRetryDelay = 100;
        /// <summary>How often should we retry sending a reliable message before throwing/logging an error. Time = NumberOfRetryTimes * SendRetryDelay</summary>
        public int NumberOfSendRetries = 10;
    }
}
