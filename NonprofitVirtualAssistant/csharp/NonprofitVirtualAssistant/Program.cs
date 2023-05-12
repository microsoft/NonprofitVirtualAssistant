using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.TeamsFx.Conversation;
using NVA;
using NVA.Bots;
using NVA.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpContextAccessor();

// Prepare Configuration for ConfigurationBotFrameworkAuthentication
builder.Configuration["MicrosoftAppType"] = "MultiTenant";
builder.Configuration["MicrosoftAppId"] = builder.Configuration.GetSection("BOT_ID")?.Value;
builder.Configuration["MicrosoftAppPassword"] = builder.Configuration.GetSection("BOT_PASSWORD")?.Value;

// Create the Bot Framework Authentication to be used with the Bot Adapter.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// Create the Cloud Adapter with error handling enabled.
// Note: some classes expect a BotAdapter and some expect a BotFrameworkHttpAdapter, so
// register the same adapter instance for both types.
builder.Services.AddSingleton<CloudAdapter, AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetService<CloudAdapter>());
builder.Services.AddSingleton<BotAdapter>(sp => sp.GetService<CloudAdapter>());

// Add conversation bot.
builder.Services.AddSingleton(sp =>
{
    var options = new ConversationOptions()
    {
        Adapter = sp.GetService<CloudAdapter>()
    };

    return new ConversationBot(options);
});

// Create the bot as a transient. In this case the ASP Controller is expecting an IBot.
builder.Services.AddTransient<IBot, Bot>();

builder.Services.AddSingleton<OpenAIService>();
builder.Services.AddSingleton<ConversationManager>();

//Create the storage we'll be using for User and Conversation state
builder.Services.AddSingleton<IStorage, MemoryStorage>();

// Create the User state
builder.Services.AddSingleton<UserState>();

//Create the Conversation state
builder.Services.AddSingleton<ConversationState>();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.Run();