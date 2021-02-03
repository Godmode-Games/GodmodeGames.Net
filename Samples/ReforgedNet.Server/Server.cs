using ReforgedNet.LL;
using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.Server
{
    public class Server
    {
        public RServerSocket Socket = null;

        public Server()
        {
        }

        public void StartListen()
        {
            var settings = new RSocketSettings();
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 7000);
            var cancellationToken = new CancellationTokenSource();

            Socket = new RServerSocket(settings, remoteEndPoint, new RJsonSerialization(), null);

            Socket.NewClientConnection += OnNewClient;
            Socket.StartListen();
            Console.WriteLine("Server started.");

            // Register receiver
            Socket.RegisterReceiver(RSocket.DEFAULT_RECEIVER_ROUTE, async (RNetMessage message) =>
            {
                Console.WriteLine($"Received from: {message.RemoteEndPoint} ,message:" + ASCIIEncoding.UTF8.GetString(message.Data));

                await Task.Delay(100);

                var messageString = "hello-client!";
                var data = ASCIIEncoding.UTF8.GetBytes(messageString);
                Socket.Send(1, ref data, message.RemoteEndPoint);

                Console.WriteLine($"Sent message: {messageString}, to: {message.RemoteEndPoint}");
            });
        }

        private void OnNewClient(EndPoint ep)
        {
            Console.WriteLine("New client connected: " + ep.ToString());
        }
    }
}
