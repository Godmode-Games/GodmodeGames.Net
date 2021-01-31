using ReforgedNet.LL;
using ReforgedNet.LL.Serialization;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReforgedNet.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Socket socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            var settings = new RSocketSettings();
            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 7000);
            var cancellationToken = new CancellationTokenSource();
            socket.Bind(remoteEndPoint);

            RSocket rsocket = new RSocket(socket, settings, new RJsonSerialization(), remoteEndPoint, null, cancellationToken.Token);

            // Register receiver
            rsocket.RegisterReceiver(RSocket.DEFAULT_RECEIVER_ROUTE, async (RNetMessage message) =>
            {
                Console.WriteLine($"Received from: {message.RemoteEndPoint} ,message:" + ASCIIEncoding.UTF8.GetString(message.Data));

                await Task.Delay(100);

                var messageString = "hello-server!";
                var data = ASCIIEncoding.UTF8.GetBytes(messageString);
                rsocket.Send(1, ref data, message.RemoteEndPoint);

                Console.WriteLine($"Sent message: {messageString}, to: {message.RemoteEndPoint}");
            });

            Console.WriteLine("Server started!");


            while (true)
            {
                rsocket.Dispatch();
                await Task.Delay(100);
            }

            rsocket.Close();
        }
    }
}
