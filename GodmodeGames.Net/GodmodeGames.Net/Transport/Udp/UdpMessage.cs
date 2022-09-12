using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace GodmodeGames.Net.Transport.Udp
{
    internal class UdpMessage : Message
    {
        internal enum EMessageType : byte { Data = 1, Ack = 2, DiscoverRequest = 3, DiscoverResponse = 4, Disconnect = 5, HeartBeat = 6 }
        internal EMessageType MessageType = EMessageType.Data;

        internal int MessageId = -1;// -1 means notreliable
        internal IPEndPoint RemoteEndpoint;

        internal UdpMessage()
        {
            this.SetPing(0);
        }

        internal UdpMessage(byte[] data, int messageid, IPEndPoint endpoint, EMessageType type)
        { 
            Data = data;
            MessageId = messageid;
            MessageType = type;
            RemoteEndpoint = endpoint;
            this.SetPing(0);
        }

        /// <summary>
        /// deserialize a byte array to message
        /// </summary>
        /// <param name="data"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        internal bool Deserialize(byte[] data, IPEndPoint endpoint)
        {
            if (data.Length < 5)
            {
                return false;
            }

            this.MessageType = (EMessageType)data[0];
            this.MessageId = BitConverter.ToInt32(data, 1);
            this.RemoteEndpoint = endpoint;

            if (data.Length > 5)
            {
                this.Data = data.Skip(5).ToArray();
            }
            else
            {
                this.Data = new byte[0];
            }

            return true;
        }

        /// <summary>
        /// returns the byte-array of the message
        /// </summary>
        /// <returns></returns>
        internal override byte[] Serialize()
        {
            List<byte> ret = new List<byte>();
            ret.Add((byte)this.MessageType);
            ret.AddRange(BitConverter.GetBytes(this.MessageId));
            if (this.Data.Length > 0)
            {
                ret.AddRange(this.Data);
            }

            return ret.ToArray();
        }
    }
}
