namespace GodmodeGames.Net.Logging
{
    public interface ILogger
    {
        void GGLogInfo(string info);

        void GGLogWarning(string warning);

        void GGLogError(string error);
    }
}
