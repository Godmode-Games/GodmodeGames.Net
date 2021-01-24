using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#nullable enable
namespace ReforgedNet.LL
{
    public class RNetMessageParameter
    {
        public byte Index;
        public object? Value;
    }

    /// <summary>
    /// Represents information about a network message.
    /// </summary>
    public class RNetMessage
    {
        /// <summary>
        /// Holds information about the invoked method name.
        /// </summary>
        public readonly string? Method;
        /// <summary>
        /// As a fallback for the message name for better compression.
        /// </summary>
        public readonly int? MessageId;
        public RNetMessageParameter[] Params = new RNetMessageParameter[0];
        public int? TransactionId;
        public EndPoint RemoteEndPoint;
        public RQoSType QoSType = RQoSType.Unrealiable;

        public RNetMessage(string method, EndPoint remoteEP)
        {
            Method = method;
            RemoteEndPoint = remoteEP;
        }

        public RNetMessage(int messageId, EndPoint remoteEP)
        {
            MessageId = messageId;
            RemoteEndPoint = remoteEP;
        }
    }
}
