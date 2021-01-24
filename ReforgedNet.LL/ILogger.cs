using System;
using System.Collections.Generic;
using System.Text;

namespace ReforgedNet.LL
{
    public class LogInfo
    {
        public DateTime OccuredDateTime;
        public string? Message;
    }

    public interface ILogger
    {
        public void WriteInfo(LogInfo info);

        public void WriteWarning(LogInfo warning);

        public void WriteError(LogInfo error);
    }
}
