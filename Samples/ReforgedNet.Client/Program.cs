using ReforgedNet.LL;
using ReforgedNet.LL.Serialization;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            var settings = new RSocketSettings();
            var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 7000);
            var cancellationToken = new CancellationTokenSource();

            RSocket rsocket = new RSocket(socket, settings, new RJsonSerialization(), new IPEndPoint(IPAddress.Any, 0), null, cancellationToken.Token);

            // Register receiver
            rsocket.RegisterReceiver(RSocket.DEFAULT_RECEIVER_ROUTE, (RNetMessage message) =>
            {
                Console.WriteLine($"Received from: {message.RemoteEndPoint} ,message:" + ASCIIEncoding.UTF8.GetString(message.Data));
            });

            Console.WriteLine("Client started.");

            var data = ASCIIEncoding.UTF8.GetBytes("hello-server!");
            rsocket.Send(RSocket.DEFAULT_RECEIVER_ROUTE, ref data, remoteEndPoint);

            while (true)
            {
                rsocket.Dispatch();
                await Task.Delay(100);
            }

            rsocket.Close();
        }
    }
}
