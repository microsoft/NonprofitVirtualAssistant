using Azure;
using Azure.AI.OpenAI;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using NVA.Enums;
using NVA.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace NVA.Services
{
    public class ConversationManager
    {
        private const string CONVERSATION_STORE_KEY = "conversations";
        private const int CHAR_LIMIT = 800;
        private static readonly char[] END_CHARS = new[] { '.', '\n' };

        private const string MODERATION_MESSAGE = "Warning: your message has been flagged for a possible content violation. Please rephrase your response and try again. Repeated violations may result in a suspension of this service.";

        private const string WAIT_MESSAGE = "The bot is currently working. Please wait until the bot has responded before sending a new message.";

        private const string RATE_LIMIT_MESSAGE = "Rate limit reached for OpenAI api. Please wait a while and try again.";

        private static readonly string[] CHAR_LIMIT_WARNINGS = new[] {
            "Sorry, your response exceeded the maximum character limit. Could you please try again with a shorter version?",
            "We love your enthusiasm, but your response is too long. Please shorten it and try again!",
            $"Your response is too long to be processed. Please try to shorten it below {CHAR_LIMIT} characters.",
            $"Oops! Your response is too lengthy for us to process. Please limit your response to {CHAR_LIMIT} characters or less.",
            $"Unfortunately, your response is too long. Please rephrase it and keep it under {CHAR_LIMIT} characters.",
            $"Sorry, your response is too wordy for us. Please try to shorten it to {CHAR_LIMIT} characters or less.",
            $"We appreciate your interest, but your response is too long. Please shorten it to {CHAR_LIMIT} characters or less and try again!",
            $"Your response is too verbose for us to process. Please try to make it shorter and under {CHAR_LIMIT} characters.",
            $"Your response is great, but it's too long! Please try to keep it under {CHAR_LIMIT} characters.",
            $"Sorry, we have a {CHAR_LIMIT} character limit. Could you please rephrase your response to fit within this limit?"
        };

        private readonly ConversationState _state;
        private readonly OpenAIService _oaiService;

        public ConversationManager(ConversationState state, OpenAIService oaiService)
        {
            _state = state;
            _oaiService = oaiService;
        }

        /// <summary>
        /// Accepts a turncontext representing a user turn and generates bot response for it, accounting for conversation history.
        /// </summary>
        /// <param name="turnContext">`ITurnContext` that represents user turn.</param>
        /// <param name="updateCallback">Callback that is called when a new sentence is received.
        /// Callback is always called with the whole message up to received till now.
        /// It's not called for the final sentence, and is not called at all if there is only one sentence.</param>
        /// <returns>Bot response</returns>
        public async Task<ConversationResponse> GenerateResponse(
            ITurnContext<IMessageActivity> turnContext, Action<string> updateCallback, CancellationToken cancellationToken)
        {
            try
            {
                using var token = new DisposableToken(turnContext.Activity.Conversation.Id);

                var question = new ChatMessage(ChatRole.User, turnContext.Activity.Text);

                // limit the size of the user question to a reasonable length
                if (question.Content.Length > CHAR_LIMIT)
                {
                    string retry = CHAR_LIMIT_WARNINGS[new Random().Next(CHAR_LIMIT_WARNINGS.Length)];
                    return new ConversationResponse(retry, ConversationResponseType.CharLimit);
                }

                // check the new message vs moderation
                if (await _oaiService.CheckModeration(question.Content, cancellationToken))
                {
                    return new ConversationResponse(MODERATION_MESSAGE, ConversationResponseType.Flagged);
                }

                // fetch user conversation history
                var conversations = _state.CreateProperty<List<MessagePair>>(CONVERSATION_STORE_KEY);
                var userConversation = await conversations.GetAsync(turnContext,
                    () => new List<MessagePair>(), cancellationToken).ConfigureAwait(false);

                var completionsOptions = ProcessInput(userConversation, question);
                var response = new StringBuilder();

                await foreach (var message in _oaiService.GetCompletion(completionsOptions, cancellationToken))
                {
                    // we don't want the event to fire for last segment, so here it's checked against the previous segment.
                    if (response.Length > 1 && END_CHARS.Contains(response[^1]))
                    {
                        updateCallback?.Invoke(response.ToString());
                    }
                    response.Append(message.Content);
                }

                var responseString = response.ToString();
                userConversation.Add(new MessagePair(question, new ChatMessage(ChatRole.Assistant, responseString)));
                // save changes to conversation history
                await _state.SaveChangesAsync(turnContext, cancellationToken: cancellationToken).ConfigureAwait(false);

                return new ConversationResponse(response.ToString(), ConversationResponseType.Chat);
            }
            catch (DisposableTokenException)
            {
                // if there is currently a bot response in processing for current conversation send back a wait message
                return new ConversationResponse(WAIT_MESSAGE, ConversationResponseType.Busy);
            }
            catch (RequestFailedException e) when (e.Status == (int)HttpStatusCode.TooManyRequests)
            {
                return new ConversationResponse(RATE_LIMIT_MESSAGE, ConversationResponseType.RateLimit);
            }
        }

        /// <summary>
        /// Appends user history to question and generates the messages to pass to api
        /// </summary>
        private static ChatCompletionsOptions ProcessInput(List<MessagePair> userConversation, ChatMessage question)
        {
            var inputLength = question.Content.Length;
            // traverse conversation history in reverse, discard after token budget is full
            for (int i = userConversation.Count - 1; i >= 0; i--)
            {
                inputLength += userConversation[i].Length;
                if (inputLength > OpenAIService.MaxInputLength)
                {
                    userConversation.RemoveRange(0, i + 1);
                    break;
                }
            }

            var completionsOptions = OpenAIService.GetCompletionOptions();
            foreach (var exchange in userConversation)
            {
                completionsOptions.Messages.Add(exchange.User);
                completionsOptions.Messages.Add(exchange.Assistant);
            }
            completionsOptions.Messages.Add(question);
            return completionsOptions;
        }

        #region Disposable Token
        private class DisposableToken : IDisposable
        {
            private static readonly ConcurrentDictionary<string, bool> _activeTokens = new();

            private readonly string _id;

            public DisposableToken(string id)
            {
                _id = id;

                if (!_activeTokens.TryAdd(id, true))
                {
                    throw new DisposableTokenException();
                }
            }

            public void Dispose()
            {
                _activeTokens.TryRemove(_id, out _);
                GC.SuppressFinalize(this);
            }
        }

        private class DisposableTokenException : Exception { }
        #endregion
    }
}
