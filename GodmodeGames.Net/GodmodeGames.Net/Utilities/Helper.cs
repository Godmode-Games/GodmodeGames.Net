using System.Text;

namespace GodmodeGames.Net.Utilities
{
    internal class Helper
    {
        /// <summary>
        /// Generates string from byte-array
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
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
