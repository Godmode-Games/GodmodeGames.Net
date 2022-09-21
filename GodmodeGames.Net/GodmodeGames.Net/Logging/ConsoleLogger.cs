using System;

namespace GodmodeGames.Net.Logging
{
    public class ConsoleLogger : ILogger
    {
        /// <summary>
        /// Log an error
        /// </summary>
        /// <param name="error"></param>
        public void GGLogError(string error)
        {
            Console.WriteLine(DateTime.Now.ToString() + " GGNet (E) " + error);
        }

        /// <summary>
        /// Log a notice
        /// </summary>
        /// <param name="info"></param>
        public void GGLogInfo(string info)
        {
            Console.WriteLine(DateTime.Now.ToString() + " GGNet (N) " + info);
        }

        /// <summary>
        /// Log a warning
        /// </summary>
        /// <param name="warning"></param>
        public void GGLogWarning(string warning)
        {
            Console.WriteLine(DateTime.Now.ToString() + " GGNet (W) " + warning);
        }
    }
}
