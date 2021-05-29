using System;

namespace ReforgedNet.LL.Logging
{
    public class ConsoleLogger : ILogger
    {
        public void WriteError(LogInfo error)
        {
            Console.WriteLine(error.OccuredDateTime.ToString() + " RSocket (E) " + error.Message);
        }

        public void WriteInfo(LogInfo info)
        {
            Console.WriteLine(info.OccuredDateTime.ToString() + " RSocket (N) " + info.Message);
        }

        public void WriteWarning(LogInfo warning)
        {
            Console.WriteLine(warning.OccuredDateTime.ToString() + " RSocket (W) " + warning.Message);
        }
    }
}
