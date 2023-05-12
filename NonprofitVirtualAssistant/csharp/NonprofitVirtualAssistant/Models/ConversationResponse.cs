using NVA.Enums;

namespace NVA.Models
{
    public class ConversationResponse
    {
        public string Message { get; }
        public ConversationResponseType Type { get; }
        public bool IsError => Type != ConversationResponseType.Chat;

        public ConversationResponse(string message, ConversationResponseType type)
        {
            Message = message;
            Type = type;
        }
    }
}
