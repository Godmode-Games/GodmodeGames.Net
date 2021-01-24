using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL.Internal
{
    internal class ReceiveDelegateDefinition
    {
        internal readonly ReceiveDelegate ReceiveDelegate;
        internal readonly int? MessageId;
        internal readonly string? Method;

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
}
