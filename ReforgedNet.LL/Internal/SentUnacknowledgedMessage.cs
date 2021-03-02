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
        internal long TransactionId { get; set; }

        public SentUnacknowledgedMessage(byte[] sentData, EndPoint remoteEndPoint, DateTime nextRetryTime, long transactionId)
        {
            SentData = sentData;
            RemoteEndPoint = remoteEndPoint;
            NextRetryTime = nextRetryTime;
            TransactionId = transactionId;
        }
    }
}
