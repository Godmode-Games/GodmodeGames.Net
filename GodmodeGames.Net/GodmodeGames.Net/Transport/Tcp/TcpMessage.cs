using System.Collections.Generic;
using System.Linq;

namespace GodmodeGames.Net.Transport.Tcp
{
    internal class TcpMessage
    {
        internal enum EMessageType : byte { Data = 1, Disconnect = 2, HeartBeatPing = 3, HeartbeatPong = 4 }
        internal EMessageType MessageType = EMessageType.Data;

        internal byte[] Data = new byte[0];
        internal GGConnection Client = null;

        internal TcpMessage()
        {

        }

        internal TcpMessage(byte[] data, GGConnection client, EMessageType type)
        {
            this.Data = data;
            this.MessageType = type;
            this.Client = client;
        }

        /// <summary>
        /// deserialize a byte array to message
        /// </summary>
        /// <param name="data"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        internal bool Deserialize(byte[] data, GGConnection client)
        {
            if (data.Length < 1)
            {
                return false;
            }

            this.MessageType = (EMessageType)data[0];
            this.Client = client;

            if (data.Length > 4)
            {
                this.Data = data.Skip(1).ToArray();
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
        internal byte[] Serialize()
        {
            List<byte> ret = new List<byte>();
            ret.Add((byte)this.MessageType);
            if (this.Data.Length > 0)
            {
                ret.AddRange(this.Data);
            }

            return ret.ToArray();
        }
    }
}
