using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#nullable enable
namespace ReforgedNet.LL
{
    /// <summary>
    /// Represents information about a network message.
    /// </summary>
    public class RNetMessage
    {
        public int MessageId;
        public byte[] Data;
        public int? TransactionId;
        public EndPoint RemoteEndPoint;
        public RQoSType QoSType = RQoSType.Unrealiable;

        public RNetMessage(int messageId, byte[] data, EndPoint remoteEP, RQoSType qosType = RQoSType.Unrealiable)
        {
            MessageId = messageId;
            Data = data;
            RemoteEndPoint = remoteEP;
            QoSType = qosType;
        }
    }
}
