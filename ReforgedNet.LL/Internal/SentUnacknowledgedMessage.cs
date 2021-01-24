using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL.Internal
{
    internal class SentUnacknowledgedMessage
    {
        internal readonly byte[] SentData;
        internal readonly EndPoint RemoteEndPoint;
        internal DateTime NextRetryTime { get; set; }
        internal int RetriedTimes { get; set; }

        public SentUnacknowledgedMessage(byte[] sentData, EndPoint remoteEndPoint)
        {
            SentData = sentData;
            RemoteEndPoint = remoteEndPoint;
        }
    }
}
