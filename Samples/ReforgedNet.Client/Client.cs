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
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 7000);
            var cancellationToken = new CancellationTokenSource();

            Socket = new RClientSocket(settings, new RByteSerialization(), null);

            Socket.Connected += OnConnectSuccessful;
            Socket.Error += OnConnectFailed;
            Socket.Connect(remoteEndPoint);
            Console.WriteLine("Client started.");
        }

        private void OnConnectFailed(long tid)
        {
            Console.WriteLine("Connection to server failed!");
        }

        private void OnConnectSuccessful()
        {
            // Register receiver
            Socket.RegisterReceiver(RSocket.DEFAULT_RECEIVER_ROUTE, (RNetMessage message) =>
            {
                Console.WriteLine($"Received from: {message.RemoteEndPoint}, message:" + ASCIIEncoding.UTF8.GetString(message.Data));
                Socket.Disconnected += OnDisconnect;
                //Socket.DisconnectAsync();
            });

            Console.WriteLine("Connection to server successful.");

            var data = ASCIIEncoding.UTF8.GetBytes("hello-server!");
            Socket.Send(RSocket.DEFAULT_RECEIVER_ROUTE, ref data, Socket.RemoteEndPoint, RQoSType.Realiable);
        }

        private void OnDisconnect()
        {
            Console.WriteLine("Connection closed");
            Socket.Close();
        }
    }
}
