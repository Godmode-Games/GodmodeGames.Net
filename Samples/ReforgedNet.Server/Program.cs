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
            Server server = new Server();
            server.StartListen();

            while (true)
            {
                server.Socket.Dispatch();
                await Task.Delay(100);
            }

            server.Socket.Close();
        }
    }
}
