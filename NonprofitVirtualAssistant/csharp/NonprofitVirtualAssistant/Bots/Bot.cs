using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NVA.Enums;
using NVA.Models;
using NVA.Services;

namespace NVA.Bots
{
    public class Bot : ActivityHandler
    {
        // Seconds to wait before starting to do incremental updates.
        private const int UPDATE_INITIAL_DELAY_SECS = 7;
        private const string CONVERSATION_TYPE_CHANNEL = "channel";

        private readonly ConversationManager _conversationManager;

        // Task source for piping incremental updates.
        private volatile TaskCompletionSource<string> _sentenceUpdate;

        public Bot(ConversationManager conversationManager)
        {
            _conversationManager = conversationManager;
            _sentenceUpdate = new TaskCompletionSource<string>();
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(turnContext.Activity.Text))
            {
                return;
            }

            // is it a chat or a channel
            bool isChannel = turnContext.Activity.Conversation.ConversationType == CONVERSATION_TYPE_CHANNEL;

            if (!isChannel)
            {
                // Bot typing indicator.
                await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken).ConfigureAwait(false);
            }

            // Intially we want to wait for a minimum time before sending an update, so combine sentence update event with delay task.
            var updateWaitTask = WaitSentenceUpdate(withDelay: true);
            // Start generating chat response.
            var generateTask = _conversationManager.GenerateResponse(turnContext, SentenceUpdateCallback, cancellationToken);

            string answerId = null;
            bool generateComplete = false;
            do
            {
                // Wait till either generation is complete or an incremental update arrives.
                var update = await Task.WhenAny(generateTask, updateWaitTask).Unwrap().ConfigureAwait(false);
                var updateMessage = MessageFactory.Text(update.Message);
                // refresh incremental update wait task
                updateWaitTask = WaitSentenceUpdate();

                // Cache the value of task completion status.
                generateComplete = generateTask.IsCompleted;

                // If it's the first update there's no activity id generated yet.
                if (string.IsNullOrEmpty(answerId))
                {
                    var response = await turnContext.SendActivityAsync(updateMessage, cancellationToken).ConfigureAwait(false);
                    answerId = response.Id;
                }
                // For subsequent updates use the same activity id.
                else
                {
                    if (generateComplete && !isChannel)
                    {
                        // When generation is complete the message we've been updating is deleted, and then the entire content is send as a new message.
                        // This raises a notification to the user when letter is complete,
                        // and serves as a workaround to `UpdateActivity` not cancelling typing indicator.
                        await Task.WhenAll(turnContext.DeleteActivityAsync(answerId, cancellationToken),
                        turnContext.SendActivityAsync(updateMessage, cancellationToken)).ConfigureAwait(false);
                    }
                    else
                    {
                        // If generation is not complete use the same activity id and update the message.
                        updateMessage.Id = answerId;
                        await turnContext.UpdateActivityAsync(updateMessage, cancellationToken).ConfigureAwait(false);
                    }
                }

                // refresh typing indicator if still generating or bot is busy
                if ((!generateComplete || update.Type == ConversationResponseType.Busy) && !isChannel)
                {
                    // Typing indicator is reset when `SendActivity` is called, so it has to be resend.
                    await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken).ConfigureAwait(false);
                }
            } while (!generateComplete);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var adaptiveCardJson = File.ReadAllText(@".\Cards\welcomeCard.json");
            JObject json = JObject.Parse(adaptiveCardJson);

            var adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(json.ToString()),
            };

            var response = MessageFactory.Attachment(adaptiveCardAttachment);
            await turnContext.SendActivityAsync(response, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ConversationResponse> WaitSentenceUpdate(bool withDelay = false)
        {
            var task = _sentenceUpdate.Task;
            if (withDelay)
            {
                await Task.WhenAll(task, Task.Delay(UPDATE_INITIAL_DELAY_SECS)).ConfigureAwait(false);
            }
            else
            {
                await task.ConfigureAwait(false);
            }
            return new ConversationResponse(task.Result, ConversationResponseType.Chat);
        }

        private void SentenceUpdateCallback(string message)
        {
            _sentenceUpdate.TrySetResult(message);
            // Replace the incremental update task source with a new instance so that we can receive further updates via the event handler.
            _sentenceUpdate = new TaskCompletionSource<string>();
        }
    }
}