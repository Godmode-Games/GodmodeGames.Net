using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
            return ASCIIEncoding.UTF8.GetString(data)?.Contains("MessageId") ?? false;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] Serialize(RNetMessage message)
        {
            return ASCIIEncoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RNetMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<RNetMessage>(ASCIIEncoding.UTF8.GetString(data));
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool IsMessageACK(byte[] data)
        {
            return ASCIIEncoding.UTF8.GetString(data)?.StartsWith("ack") ?? false;
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] SerializeACKMessage(RReliableNetMessageACK message)
        {
            return ASCIIEncoding.UTF8.GetBytes("ack" + JsonConvert.SerializeObject(message));
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RReliableNetMessageACK DeserializeACKMessage(byte[] data)
        {
            return JsonConvert.DeserializeObject<RReliableNetMessageACK>(ASCIIEncoding.UTF8.GetString(data).Remove(0, 3));
        }
    }
}
