//#define GG_SERVER_DISCONNECT //should the server disconnect clients after 5 seconds?
//#define GG_SERVER_SHUTDOWN //shut down server after 5 seconds?

using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using System.Net;
using System.Text;

namespace GodmodeGames.Net.SampleServer
{
    class Program
    {
        static GGServerListener Server = null;
        static async Task Main(string[] args)
        {
            Server = new GGServerListener(new ServerSocketSettings(), new ConsoleLogger());

            Server.ReceivedData += OnData;
            Server.ClientConnected += OnClientConnect;
            Server.ClientDisconnected += OnClientDisconnect;
            Server.ShutdownCompleted += OnShutdown;
            Server.StartListening(new IPEndPoint(IPAddress.Any, 7000));

            long count = 0;
            while (true)
            {
                Server.Tick();
                count += 50;
#if GG_SERVER_DISCONNECT
                if (Server.Connections.Count > 0)
                {   
                    if (count == 5000)
                    {
                        Server.DisconnectClient(Server.Connections.First().Value, "Off he goes...");
                    }
                }
#elif GG_SERVER_SHUTDOWN
	            if (Server.IsListening == true)
                {
                    if (count == 5000)
                    {
                        Server.ShutdownAsync();
                    }
                }
#endif
                //show ping every 5 seconds
                if (count % 5000 == 0)
                {
                    if (Server.IsListening && Server.Connections.Count > 0)
                    {
                        foreach (var connection in Server.ConnectionsArray)
                        {
                            Console.WriteLine(connection.ToString() + " has a ping of " + connection.Ping + "ms");
                        }
                    }
                }
                await Task.Delay(50);
            }
        }

        private static void OnShutdown()
        {
            Console.WriteLine("Shutdown Completed");
        }

        private static void OnClientConnect(GGConnection connection)
        {
            Console.WriteLine("New connection from " + connection.ClientEndpoint.ToString());
        }

        private static void OnClientDisconnect(GGConnection connection, string reason)
        {
            Console.WriteLine("Client " + connection.ClientEndpoint.ToString() + " disconnected: " + reason);
        }

        private static void OnData(byte[] data, GGConnection connection)
        {
            Console.WriteLine("Received from client " + connection.ClientEndpoint.ToString() + ": \"" + Encoding.UTF8.GetString(data) + "\"");
            connection.Send(Encoding.UTF8.GetBytes("Welcome Client!"));
        }
    }
}
