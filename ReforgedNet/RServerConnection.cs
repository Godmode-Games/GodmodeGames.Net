using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ReforgedNet
{
    public class RServerConnection : RSocket
    {
        public int Answer(RBasePacket packet)
        {
            return Send(null, packet).Result;
        }

        public void RegisterRequestAction(RequestDelegate @delegate)
        {

        }
    }
}
