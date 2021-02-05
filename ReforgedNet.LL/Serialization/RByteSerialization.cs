using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ReforgedNet.LL.Serialization
{
    public class RByteSerialization : IPacketSerializer
    {
        public RNetMessage Deserialize(byte[] data, EndPoint remoteEndPoint)
        {
            int? MsgId = null;
            int? TransactionId = null;
            RQoSType type = RQoSType.Realiable;

            int readCursor = 1;//byte[0] = 1 -> RNetMessage

            if (data[readCursor++] == 1)
            {
                MsgId = BitConverter.ToInt32(data, readCursor);
                readCursor += sizeof(int);
            } //else MsgId = null; 

            if (data[readCursor++] == 1)
            {
                TransactionId = BitConverter.ToInt32(data, readCursor);
                readCursor += sizeof(int);
            } //else TransactionId = null
            

            type = (RQoSType)BitConverter.ToInt32(data, readCursor);
            readCursor += sizeof(int);

            int data_length = BitConverter.ToInt32(data, readCursor);
            readCursor += sizeof(int);

            byte[] rec_data = new byte[data_length];
            Array.Copy(data, readCursor, rec_data, 0, data_length);

            return new RNetMessage(MsgId, rec_data, TransactionId, remoteEndPoint, type, null);
        }

        public RReliableNetMessageACK DeserializeACKMessage(byte[] data, EndPoint remoteEndPoint)
        {
            int? MsgId = null;
            int TransactionId = -1;

            int readCursor = 1;//byte[0] = 0 -> ACKMessage
            if (data[readCursor++] == 1)
            {
                MsgId = BitConverter.ToInt32(data, readCursor);
                readCursor += sizeof(int);
            }

            TransactionId = BitConverter.ToInt32(data, readCursor);
            readCursor += sizeof(int);

            return new RReliableNetMessageACK(MsgId, TransactionId, remoteEndPoint);
        }

        public bool IsMessageACK(byte[] data)
        {
            return !IsRequest(data);
        }

        public bool IsRequest(byte[] data)
        {
            if (data[0] == 1)
            {
                return true;
            }
            return false;
        }

        public byte[] Serialize(RNetMessage message)
        {
            List<byte> bytes = new List<byte>();

            //First Byte -> 1 if NetMessage, 0 if ACK
            bytes.Add(1);

            //nullable MessageId
            if (message.MessageId == null)
            {
                bytes.Add(0); //Discover Packet
            }
            else
            {
                bytes.Add(1); //RNetMessage
                bytes.AddRange(BitConverter.GetBytes((int)message.MessageId));
            }

            //nullable TransactionId
            if (message.TransactionId == null)
            {
                bytes.Add(0);
            }
            else
            {
                bytes.Add(1);
                bytes.AddRange(BitConverter.GetBytes((int)message.TransactionId));
            }

            //QoSType
            bytes.AddRange(BitConverter.GetBytes((int)message.QoSType));

            //Data
            bytes.AddRange(BitConverter.GetBytes(message.Data.Length));
            bytes.AddRange(message.Data);

            return bytes.ToArray();
        }

        public byte[] SerializeACKMessage(RReliableNetMessageACK message)
        {
            List<byte> bytes = new List<byte>();

            //First Byte -> 1 if NetMessage, 0 if ACK
            bytes.Add(0);

            //nullable MessageId
            if (message.MessageId == null)
            {
                bytes.Add(0); //Discover Packet
            }
            else
            {
                bytes.Add(1); //RNetMessage
                bytes.AddRange(BitConverter.GetBytes((int)message.MessageId));
            }

            //TransactionId
            bytes.AddRange(BitConverter.GetBytes(message.TransactionId));

            return bytes.ToArray();
        }
    }
}
