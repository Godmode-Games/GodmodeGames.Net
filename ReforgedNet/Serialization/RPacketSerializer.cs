using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.Serialization
{
    public abstract class RPacketSerializer
    {
        public abstract byte[] Serialize(RBasePacket packet);
        public abstract T Deserialize<T>(byte[] data) where T : RBasePacket;
    }
}
