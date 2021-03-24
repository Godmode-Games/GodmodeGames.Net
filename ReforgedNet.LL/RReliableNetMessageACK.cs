using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL
{
    /// <summary>
    /// Network message to tell notify remote endpoint of received messages.
    /// </summary>
    public class RReliableNetMessageACK
    {
        public readonly long TransactionId;
        public readonly EndPoint RemoteEndPoint;

        public RReliableNetMessageACK(long transactionId, EndPoint remoteEndPoint)
        {
            TransactionId = transactionId;
            RemoteEndPoint = remoteEndPoint;
        }
    }
}
