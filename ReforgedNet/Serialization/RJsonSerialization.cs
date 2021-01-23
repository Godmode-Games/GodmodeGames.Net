using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.Serialization
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
    }
}
