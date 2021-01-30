using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL.Serialization
{
    public interface IPacketSerializer
    {
        /// <summary>
        /// Serializes network message.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] Serialize(RNetMessage message);
        /// <summary>
        /// Deserializes network messages.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RNetMessage Deserialize(byte[] data, EndPoint remoteEndPoint);
        /// <summary>
        /// Returns true if given byte array is a request.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool IsRequest(byte[] data);
        /// <summary>
        /// Returns true if given byte array is a message acknowledgement.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool IsMessageACK(byte[] data);
        /// <summary>
        /// Serliazes message acknowledgement.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] SerializeACKMessage(RReliableNetMessageACK message);
        /// <summary>
        /// Deserializes message acknowledgement.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public RReliableNetMessageACK DeserializeACKMessage(byte[] data, EndPoint remoteEndPoint);
    }
}
