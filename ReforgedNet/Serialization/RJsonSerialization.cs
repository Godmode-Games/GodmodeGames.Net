using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.Serialization
{
    public class RJsonSerialization : RPacketSerializer
    {
        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <returns></returns>
        public override T Deserialize<T>(byte[] data)
        {
            return JsonConvert.DeserializeObject<T>(ASCIIEncoding.UTF8.GetString(data));
        }

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public override byte[] Serialize(RBasePacket packet)
        {
            return ASCIIEncoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
        }
    }
}
