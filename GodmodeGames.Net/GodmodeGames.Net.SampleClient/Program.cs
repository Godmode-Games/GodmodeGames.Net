//#define GG_SYNCHRONUS //synchronous or asynchronous connect
//#define GG_CLIENT_DISCONNECT //should the client disconnect after 5 seconds?

using GodmodeGames.Net.Logging;
using GodmodeGames.Net.Settings;
using System.Text;

namespace GodmodeGames.Net.SampleClient
{
    class Program
    {
        static GGClient Client = null;
        static async Task Main(string[] args)
        {
            Client = new GGClient(new ClientSocketSettings(), new ConsoleLogger());
            Client.ReceivedData += OnData;
            Client.Disconnected += OnDisconnect;

#if GG_SYNCHRONUS
            if (!Client.Connect("127.0.0.1", 7000))
            {
                Console.WriteLine("Could not connect to server!");
            }
            else
            {
                Client.Send(Encoding.UTF8.GetBytes("Hello Server!"));
            }
#else

            Client.ConnectAttempt += OnConnect;
            Client.ConnectAsync("127.0.0.1", 7000);
#endif

            int count = 0;
            while (true)
            {
                Client.Tick();
                count += 50;
#if GG_CLIENT_DISCONNECT
                //disconnect after 5 sec...
                if (Client.IsConnected)
                {
                    if (count == 5000)
                    {
#if GG_SYNCHRONUS
                        if (Client.Disconnect("Goodbye!"))
                        {
                            Console.WriteLine("Disconnect successful");
                        }
#else
                        Client.DisconnectAsync("Goodbye!");
#endif
                    }
                }
#endif

                //show ping every 5 seconds
                if (count % 5000 == 0)
                {
                    if (Client.IsConnected)
                    {
                        Console.WriteLine("My ping is " + Client.Ping + "ms");
                    }
                }
                await Task.Delay(50);
            }
        }

        private static void OnData(byte[] data)
        {
            Console.WriteLine("Received from server " + Client.ServerEndpoint.ToString() + ": \"" + Encoding.UTF8.GetString(data) + "\"");
        }

        private static void OnConnect(bool success)
        {
            if (success)
            {
                Client.Send(Encoding.UTF8.GetBytes("Hello Server!"));
            }
        }

        private static void OnDisconnect(GGClient.EDisconnectBy disconnect_by, string reason)
        {
#if GG_SYNCHRONUS
#else
            switch (disconnect_by)
            {
                case GGClient.EDisconnectBy.Client:
                    Console.WriteLine("Disconnect successful");
                    break;
                case GGClient.EDisconnectBy.Server:
                    Console.WriteLine("Server closed connection with reason: " + reason);
                    break;
                case GGClient.EDisconnectBy.ConnectionLost:
                    Console.WriteLine("Connection to server lost!");
                    break;
            }
#endif
        }
    }
}
