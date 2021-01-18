using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet
{
    public static class RBasePacketExt
    {
        public static T CreateAnswerFromRequest<T>(this RBasePacket request)
            where T : RBasePacket, new()
        {
            return new T()
            {
                TransactionId = request.TransactionId
            };
        }
    }
}
