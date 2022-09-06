using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace GodmodeGames.Net.Transport
{
    internal class Message : IEquatable<Message>
    {
        public enum EMessageType : byte { Data = 1, Ack = 2, DiscoverRequest = 3, DiscoverResponse = 4, Disconnect = 5, HeartBeat = 6 }
        public EMessageType MessageType = EMessageType.Data;

        public byte[] Data = new byte[0];
        public int MessageId = -1;// -1 means notreliable
        public IPEndPoint RemoteEndpoint;

        public Message()
        {

        }

        public Message(byte[] data, int messageid, IPEndPoint endpoint, EMessageType type)
        {
            Data = data;
            MessageId = messageid;
            MessageType = type;
            RemoteEndpoint = endpoint;
        }

        /// <summary>
        /// deserialize a byte array to message
        /// </summary>
        /// <param name="data"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public bool Deserialize(byte[] data, IPEndPoint endpoint)
        {
            if (data.Length < 5)
            {
                return false;
            }

            MessageType = (EMessageType)data[0];
            MessageId = BitConverter.ToInt32(data, 1);
            RemoteEndpoint = endpoint;

            if (data.Length > 5)
            {
                Data = data.Skip(5).ToArray();
            }
            else
            {
                Data = new byte[0];
            }

            return true;
        }

        /// <summary>
        /// returns the byte-array of the message
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize()
        {
            List<byte> ret = new List<byte>();
            ret.Add((byte)MessageType);
            ret.AddRange(BitConverter.GetBytes(MessageId));
            if (Data.Length > 0)
            {
                ret.AddRange(Data);
            }

            return ret.ToArray();
        }

        public bool Equals(Message other)
        {
            return this.MessageType == other.MessageType && this.MessageId == other.MessageId && this.RemoteEndpoint == other.RemoteEndpoint && this.Data == other.Data;
        }
    }
}
