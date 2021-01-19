using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public delegate void RequestDelegate(RBasePacket packet);
    public delegate void GenericRequestDelegate<T>(T packet);
}
