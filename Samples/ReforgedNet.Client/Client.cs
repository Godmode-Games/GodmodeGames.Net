using ReforgedNet.LL;
using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

namespace ReforgedNet.Client
{
    public class Client
    {
        public RClientSocket Socket = null;

        public Client()
        {

        }

        public void Connect()
        {
            var settings = new RSocketSettings();
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 7000);
            var cancellationToken = new CancellationTokenSource();

            Socket = new RClientSocket(settings, new RJsonSerialization(), null);

            Socket.ConnectionSuccessful += OnConnectSuccessful;
            Socket.Connect(remoteEndPoint);
            Console.WriteLine("Client started.");
        }

        private void OnConnectSuccessful()
        {
            // Register receiver
            Socket.RegisterReceiver(RSocket.DEFAULT_RECEIVER_ROUTE, (RNetMessage message) =>
            {
                Console.WriteLine($"Received from: {message.RemoteEndPoint} ,message:" + ASCIIEncoding.UTF8.GetString(message.Data));
            });

            Console.WriteLine("Connected...");

            var data = ASCIIEncoding.UTF8.GetBytes("hello-server!");
            Socket.Send(RSocket.DEFAULT_RECEIVER_ROUTE, ref data, Socket.RemoteEndPoint);
        }
    }
}
