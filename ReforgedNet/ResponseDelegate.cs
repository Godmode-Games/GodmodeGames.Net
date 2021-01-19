using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public delegate void ResponseDelegate(RBasePacket packet);
    public delegate void GenericResponseDelegate<T>(T packet);
}
