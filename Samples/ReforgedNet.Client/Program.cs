using System.Threading.Tasks;

namespace GodmodeGames.Net.Client
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
        }
    }
}
