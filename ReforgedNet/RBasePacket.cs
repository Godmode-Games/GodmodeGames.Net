using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public abstract class RBasePacket
    {
        /// <summary>
        /// PacketId is used to differ between different types of <see cref="RBasePacket"/>
        /// </summary>
        public int PacketId;

        /// <summary>
        /// TransactionId is used to identify is response action.
        /// </summary>
        public int TransactionId;

        public RBasePacket(int packetId, int? transactionId = default)
        {
            PacketId = packetId;
            TransactionId = transactionId ?? RTransactionGenerator.GenerateId();
        }
    }
}
