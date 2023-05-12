using Azure.AI.OpenAI;
using System.Diagnostics;

namespace NVA.Models
{
    public class MessagePair
    {
        public ChatMessage User { get; }
        public ChatMessage Assistant { get; }

        public MessagePair(ChatMessage user, ChatMessage assistant)
        {
            Debug.Assert(user.Role == ChatRole.User);
            Debug.Assert(assistant.Role == ChatRole.Assistant);

            User = user;
            Assistant = assistant;
        }

        public int Length => User.Content.Length + Assistant.Content.Length;
    }
}
