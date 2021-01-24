using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    /// <summary>
    /// Network message to identify received messages.
    /// </summary>
    public class RReliableNetMessageACK
    {
        public readonly int? MessageId;
        public readonly string? Method;
        public readonly int TransactionId;

        public RReliableNetMessageACK(int messageId, int transactionId)
        {
            MessageId = messageId;
            TransactionId = transactionId;
        }

        public RReliableNetMessageACK(string method, int transactionId)
        {
            Method = method;
            TransactionId = transactionId;
        }
    }
}
