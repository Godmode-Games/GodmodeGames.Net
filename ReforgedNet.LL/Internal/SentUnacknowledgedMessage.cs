using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL.Internal
{
    public class SentUnacknowledgedMessage
    {
        internal readonly byte[] SentData;
        internal readonly EndPoint RemoteEndPoint;
        internal DateTime NextRetryTime { get; set; }
        internal int RetriedTimes { get; set; }
        internal Action? FailedCallback = null;

        public SentUnacknowledgedMessage(byte[] sentData, EndPoint remoteEndPoint, DateTime nextRetryTime, Action? failCallback = null)
        {
            SentData = sentData;
            RemoteEndPoint = remoteEndPoint;
            NextRetryTime = nextRetryTime;
            FailedCallback = failCallback;
        }
    }
}
