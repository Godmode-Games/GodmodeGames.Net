using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public enum RThreadingType
    {
        UseOnlySeperatedReceiveThread,

        UseCallerThread,
        UseSeperatedSendReceiveThread,
        UseSeperatedReceiveThread,
    }
}
