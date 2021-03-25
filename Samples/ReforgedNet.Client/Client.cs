using ReforgedNet.LL;
using ReforgedNet.LL.Serialization;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using static ReforgedNet.LL.RClientSocket;

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
            Socket.Disconnected += OnDisconnect;
            Console.WriteLine("Client started.");
        }

        private void OnConnectFailed(long tid)
        {
            Console.WriteLine("Connection to server failed!");
        }

        private void OnConnectSuccessful()
        {
            Socket.Disconnected += this.OnDisconnect;

            // Register receiver
            Socket.OnReceiveData = (byte[] data, EndPoint ep) =>
            {
                Console.WriteLine($"Received from: {ep}, message:" + ASCIIEncoding.UTF8.GetString(data));
                Socket.Disconnect();
                Console.WriteLine("closed connection");
            };

            Console.WriteLine("Connection to server successful.");

            var data = ASCIIEncoding.UTF8.GetBytes("hello-server!");
            Socket.Send(ref data, Socket.RemoteEndPoint, RQoSType.Realiable);
        }

        private void OnDisconnect(EDisconnectedBy disconnected_by)
        {
            if (disconnected_by == EDisconnectedBy.Server)
            {
                Console.WriteLine("Connection closed by server");
            }
        }
    }
}
