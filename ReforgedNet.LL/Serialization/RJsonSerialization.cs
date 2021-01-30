using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL.Serialization
{
    public class RJsonSerialization : IPacketSerializer
    {
        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool IsRequest(byte[] data)
        {
            return ASCIIEncoding.UTF8.GetString(data).Contains("data");
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] Serialize(RNetMessage message)
        {
            JObject json = new JObject();
            json["qos"] = (int)message.QoSType;
            json["msgid"] = message.MessageId;

            if (message.TransactionId.HasValue)
            {
                json["transactionId"] = message!.TransactionId;
            }

            json["data"] = message.Data;
           

            return ASCIIEncoding.UTF8.GetBytes(json.ToString());
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RNetMessage Deserialize(byte[] data, EndPoint remoteEndPoint)
        {
            JObject json = new JObject(ASCIIEncoding.UTF8.GetString(data));

            var message = new RNetMessage(
                json["msgId"]!.ToObject<int>(),
                json["data"]?.ToObject<byte[]>(),
                remoteEndPoint,
                (RQoSType)json["qos"]!.ToObject<int>()
            );

            if (json.ContainsKey("transactionId"))
            {
                message.TransactionId = json["transactionId"]!.ToObject<int>();
            }

            return message;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool IsMessageACK(byte[] data)
        {
            return !IsRequest(data);
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] SerializeACKMessage(RReliableNetMessageACK message)
        {
            JObject json = new JObject();
            json["msgid"] = message.MessageId;
            json["transactionId"] = message.TransactionId;

            return ASCIIEncoding.UTF8.GetBytes(json.ToString());
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RReliableNetMessageACK DeserializeACKMessage(byte[] data, EndPoint remoteEndPoint)
        {
            JObject json = new JObject(ASCIIEncoding.UTF8.GetString(data));

            return new RReliableNetMessageACK(
                json["msgId"]!.ToObject<int>(),
                json["transactionId"]!.ToObject<int>(),
                remoteEndPoint
            );
        }
    }
}
