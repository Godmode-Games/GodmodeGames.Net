using Newtonsoft.Json;
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
        public string? Method;
        /// <summary>
        /// As a fallback for the message name for better compression.
        /// </summary>
        public int? MessageId;
        public byte[] Data;
        public int? TransactionId;
        [JsonIgnore]
        public EndPoint RemoteEndPoint;
        [JsonIgnore]
        public RQoSType QoSType = RQoSType.Unrealiable;

        public RNetMessage()
        {
            //Für den De-Serializer
            Method = null;
            Data = new byte[0];
            RemoteEndPoint = new IPEndPoint(IPAddress.Any, 5000);
            QoSType = RQoSType.Unrealiable;
        }

        public RNetMessage(string method, byte[] data, EndPoint remoteEP, RQoSType qosType = RQoSType.Unrealiable)
        {
            Method = method;
            Data = data;
            RemoteEndPoint = remoteEP;
            QoSType = qosType;
        }

        public RNetMessage(int messageId, byte[] data, EndPoint remoteEP, RQoSType qosType = RQoSType.Unrealiable)
        {
            MessageId = messageId;
            Data = data;
            RemoteEndPoint = remoteEP;
            QoSType = qosType;
        }
    }
}
