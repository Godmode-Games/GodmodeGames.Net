using System;
using System.Collections.Generic;
using System.Net;

namespace GodmodeGames.Net.Serialization
{
    public class RByteSerialization : IPacketSerializer
    {
        public RNetMessage? Deserialize(byte[] data, IPEndPoint remoteEndPoint, out EDeserializeError error)
        {
            error = EDeserializeError.None;
            int DataSize = data.Length;

            long? TransactionId = null;
            RQoSType type = RQoSType.Realiable;

            int readCursor = 1;//byte[0] = 1 -> RNetMessage

            if (data[readCursor++] == 1)
            {
                TransactionId = BitConverter.ToInt64(data, readCursor);
                readCursor += sizeof(long);
            } //else TransactionId = null
            
            if (readCursor > DataSize)
            {
                error = EDeserializeError.NotComplete;
                return null;
            }

            type = (RQoSType)data[readCursor++];

            if (readCursor > DataSize)
            {
                error = EDeserializeError.NotComplete;
                return null;
            }

            int data_length = BitConverter.ToInt32(data, readCursor);
            readCursor += sizeof(int);

            if (readCursor + data_length > DataSize)
            {
                error = EDeserializeError.NotComplete;
                return null;
            }

            byte[] rec_data = new byte[data_length];
            Array.Copy(data, readCursor, rec_data, 0, data_length);

            return new RNetMessage(rec_data, TransactionId, remoteEndPoint, type);
        }

        public RReliableNetMessageACK DeserializeACKMessage(byte[] data, EndPoint remoteEndPoint)
        {
            long TransactionId = -1;

            int readCursor = 1;//byte[0] = 0 -> ACKMessage

            TransactionId = BitConverter.ToInt64(data, readCursor);
            //readCursor += sizeof(long);

            return new RReliableNetMessageACK(TransactionId, remoteEndPoint);
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

            //nullable TransactionId
            if (message.TransactionId == null)
            {
                bytes.Add(0);
            }
            else
            {
                bytes.Add(1);
                bytes.AddRange(BitConverter.GetBytes(message.TransactionId.Value));
            }

            //QoSType
            bytes.Add((byte)message.QoSType);

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

            //TransactionId
            bytes.AddRange(BitConverter.GetBytes(message.TransactionId));

            return bytes.ToArray();
        }
    }
}
