using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#nullable enable
namespace ReforgedNet.LL
{
    /// <summary>
    /// Holds information about network message.
    /// </summary>
    public class RNetMessage : IEquatable<RNetMessage> 
    {
        /// <summary>Message id is used to identify different types of message types, message is discovermessage if null</summary>
        public readonly int? MessageId;
        /// <summary>Byte array of transmitted content.</summary>
        public readonly byte[] Data;
        /// <summary>Transaction id to identify reliable messages.</summary>
        public readonly long? TransactionId;
        public readonly EndPoint RemoteEndPoint;
        public readonly RQoSType QoSType;

        public RNetMessage(int? messageId, byte[] data, long? transactionId, EndPoint remoteEP, RQoSType qosType = RQoSType.Unrealiable)
        {
            MessageId = messageId;
            Data = data;
            TransactionId = transactionId;
            RemoteEndPoint = remoteEP;
            QoSType = qosType;
        }

        public bool Equals(RNetMessage other)
        {
            return MessageId == other.MessageId &&
                   Data == other.Data &&
                   TransactionId == other.TransactionId &&
                   RemoteEndPoint.Equals(other.RemoteEndPoint) &&
                   QoSType == other.QoSType;
        }
    }
}
