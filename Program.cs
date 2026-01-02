using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot;
using MyDiscordBot.Data;
using MyDiscordBot.Services;

var builder = Host.CreateApplicationBuilder(args);

var socketConfig = new DiscordSocketConfig
{
    GatewayIntents =
        GatewayIntents.AllUnprivileged
        | GatewayIntents.GuildMembers
        | GatewayIntents.MessageContent
        | GatewayIntents.GuildMessages,
    AlwaysDownloadUsers = true
};

builder.Services.AddSingleton(socketConfig);
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(
    x.GetRequiredService<DiscordSocketClient>()
));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    if (!Directory.Exists("db_storage"))
    {
        Directory.CreateDirectory("db_storage");
    }
    connectionString = "Data Source=db_storage/bagd_jam.db";
}

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LevelingService>();
builder.Services.AddHostedService<BotWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

await host.RunAsync();
