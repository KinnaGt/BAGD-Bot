using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyDiscordBot;
using MyDiscordBot.Data;

var builder = Host.CreateApplicationBuilder(args);

// Configuración de Discord
var socketConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
    AlwaysDownloadUsers = true
};

builder.Services.AddSingleton(socketConfig);
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(x => new InteractionService(
    x.GetRequiredService<DiscordSocketClient>()
));

// ---> FIX: Usamos Factory en lugar de AddDbContext directo
// Esto permite crear contextos "on-the-fly" dentro de los singletons
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("Data Source=bagd_jam.db")
);

builder.Services.AddMemoryCache();
builder.Services.AddHostedService<BotWorker>();

var host = builder.Build();

// Migración inicial usando la Factory
using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

await host.RunAsync();
