using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL.Internal
{
    internal class ReceiveDelegateDefinition
    {
        internal readonly ReceiveDelegate ReceiveDelegate;
        internal readonly int? MessageId;

        internal ReceiveDelegateDefinition(int messageId, ReceiveDelegate @delegate)
        {
            MessageId = messageId;
            ReceiveDelegate = @delegate;
        }
    }
}
