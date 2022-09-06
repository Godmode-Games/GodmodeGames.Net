namespace GodmodeGames.Net.Logging
{
    public interface ILogger
    {
        void LogInfo(string info);

        void LogWarning(string warning);

        void LogError(string error);
    }
}
