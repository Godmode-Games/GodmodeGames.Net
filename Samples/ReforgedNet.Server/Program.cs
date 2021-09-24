using System.Threading.Tasks;

namespace GodmodeGames.Net.Server
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
