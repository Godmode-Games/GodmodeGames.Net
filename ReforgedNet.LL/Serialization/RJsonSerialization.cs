using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL.Serialization
{
    public class RJsonSerialization : IPacketSerializer
    {
        public bool IsRequest(byte[] data)
        {
            return ASCIIEncoding.UTF8.GetString(data)?.Contains("method") ?? false;
        }

        public byte[] Serialize(RNetMessage message)
        {
            return ASCIIEncoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
        }

        public RNetMessage Deserialize(byte[] data)
        {
            return JsonConvert.DeserializeObject<RNetMessage>(ASCIIEncoding.UTF8.GetString(data));
        }

        public bool IsValidReliableMessageACK(byte[] data)
        {
            return ASCIIEncoding.UTF8.GetString(data)?.StartsWith("ack") ?? false;
        }

        public byte[] SerializeACKMessage(RReliableNetMessageACK message)
        {
            return ASCIIEncoding.UTF8.GetBytes("ack" + JsonConvert.SerializeObject(message));
        }

        public RReliableNetMessageACK DeserializeACKMessage(byte[] data)
        {
            return JsonConvert.DeserializeObject<RReliableNetMessageACK>(ASCIIEncoding.UTF8.GetString(data).Remove(0, 3));
        }
    }
}
