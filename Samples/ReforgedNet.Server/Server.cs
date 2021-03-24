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

            Socket = new RServerSocket(settings, remoteEndPoint, new RByteSerialization(), null);

            Socket.ClientDiscoverMessage += OnNewClient;
            Socket.ClientDisconnected += OnCloseClient;
            Socket.StartListen();
            Console.WriteLine("Server started.");

            // Register receiver
            Socket.OnReceiveData = async (byte[] data, EndPoint ep) => 
            {
                Console.WriteLine($"Received from: {ep}, message: " + ASCIIEncoding.UTF8.GetString(data));

                await Task.Delay(100);

                string message = "hello-client";
                var messagedata = Encoding.ASCII.GetBytes("hello-client!");
                Socket.Send(ref messagedata, ep, RQoSType.Realiable);

                Console.WriteLine($"Sent message: {message}, to: {ep}");

                this.Socket.DisconnectEndPoint(ep);
            };
        }

        private void OnCloseClient(EndPoint ep, bool by_client)
        {
            if (by_client)
            {
                Console.WriteLine("Connection closed by " + ep.ToString());
            }
            else
            {
                Console.WriteLine("Server disconnects " + ep.ToString());
            }
        }

        private void OnNewClient(EndPoint ep)
        {
            Console.WriteLine("New client connected: " + ep.ToString());
        }
    }
}
