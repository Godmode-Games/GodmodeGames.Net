using System;

namespace ReforgedNet.LL.Logging
{
    public class LogInfo
    {
        public DateTime OccuredDateTime = DateTime.Now;
        public string? Message;

        public LogInfo() { }

        public LogInfo(string message)
        {
            Message = message;
        }
    }

    public interface ILogger
    {
        public void WriteInfo(LogInfo info);

        public void WriteWarning(LogInfo warning);

        public void WriteError(LogInfo error);
    }
}
