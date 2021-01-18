using ReforgedNet.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public class RClientSettings
    {
        /// <summary>
        /// Holds information about the thread on which the responses should be invoked.
        /// </summary>
        public RThreadingType ThreadingType;

        public RPacketSerializer Serializer;
    }
}
