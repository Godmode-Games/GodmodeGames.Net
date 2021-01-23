using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL.Serialization
{
    public interface IPacketSerializer
    {
        public byte[] Serialize(RNetMessage message);
        public RNetMessage Deserialize(byte[] data);
        public bool IsRequest(byte[] data);
        public bool IsValidReliableMessageACK(byte[] data);
    }
}
