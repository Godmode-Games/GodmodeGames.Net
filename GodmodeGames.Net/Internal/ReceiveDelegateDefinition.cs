namespace GodmodeGames.Net.Internal
{
    public class ReceiveDelegateDefinition
    {
        internal readonly ReceiveDelegate ReceiveDelegate;
        internal readonly int? MessageId;

        internal ReceiveDelegateDefinition(int? messageId, ReceiveDelegate @delegate)
        {
            MessageId = messageId;
            ReceiveDelegate = @delegate;
        }
    }
}
