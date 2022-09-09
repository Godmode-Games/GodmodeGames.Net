using System.Text;

namespace GodmodeGames.Net.Utilities
{
    internal class Helper
    {
        internal static string BytesToString(byte[] data)
        {
            if (data.Length == 0)
            {
                return null;
            }
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }
    }
}
