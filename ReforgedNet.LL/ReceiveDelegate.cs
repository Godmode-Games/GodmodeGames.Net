using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    internal class ReceiveDelegateDefinition
    {
        public readonly ReceiveDelegate ReceiveDelegate;
        public readonly int? MessageId;
        public readonly string? Method;

        internal ReceiveDelegateDefinition(int messageId, ReceiveDelegate @delegate)
        {
            MessageId = messageId;
            ReceiveDelegate = @delegate;
        }

        internal ReceiveDelegateDefinition(string method, ReceiveDelegate @delegate)
        {
            Method = method;
            ReceiveDelegate = @delegate;
        }
    }

    public delegate void ReceiveDelegate(RNetMessage message);
}
