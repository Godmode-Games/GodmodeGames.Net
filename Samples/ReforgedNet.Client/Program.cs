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
            Client client = new Client();
            client.Connect();

            while (true)
            {
                client.Socket.Dispatch();
                await Task.Delay(100);
            }

            client.Socket.Close();
        }
    }
}
