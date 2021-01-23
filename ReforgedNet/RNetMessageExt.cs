using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    public static class RNetMessageExt
    {
        public static RNetMessage CreateResponseToRequest(this RNetMessage request, string method)
        {
            return new RNetMessage(method, request.RemoteEndPoint)
            {
                TransactionId = RTransactionGenerator.GenerateId()
            };
        }

        public static RNetMessage CreateResponseToRequest(this RNetMessage request, int messageId)
        {
            return new RNetMessage(messageId, request.RemoteEndPoint)
            {
                TransactionId = RTransactionGenerator.GenerateId()
            };
        }
    }
}
