using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MyDiscordBot;

var builder = Host.CreateApplicationBuilder(args);

var socketConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.Guilds,
    LogGatewayIntentWarnings = false
};

builder.Services.AddSingleton(socketConfig);
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(
    x.GetRequiredService<DiscordSocketClient>()
));

builder.Services.AddHostedService<BotWorker>();

var host = builder.Build();
await host.RunAsync();
