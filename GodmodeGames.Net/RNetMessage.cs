using System;
using System.Net;

#nullable enable
namespace GodmodeGames.Net
{
    /// <summary>
    /// Holds information about network message.
    /// </summary>
    public class RNetMessage : IEquatable<RNetMessage> 
    {
        /// <summary>Byte array of transmitted content.</summary>
        public readonly byte[] Data;
        /// <summary>Transaction id to identify reliable messages.</summary>
        public readonly long? TransactionId;
        public readonly EndPoint RemoteEndPoint;
        public readonly RQoSType QoSType;

        public RNetMessage(byte[] data, long? transactionId, EndPoint remoteEP, RQoSType qosType = RQoSType.Unrealiable)
        {
            Data = data;
            TransactionId = transactionId;
            RemoteEndPoint = remoteEP;
            QoSType = qosType;
        }

        public bool Equals(RNetMessage other)
        {
            return Data == other.Data &&
                   TransactionId == other.TransactionId &&
                   RemoteEndPoint.Equals(other.RemoteEndPoint) &&
                   QoSType == other.QoSType;
        }
    }
}
