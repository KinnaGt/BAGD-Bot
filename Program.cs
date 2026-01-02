using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot;
using MyDiscordBot.Data;
using MyDiscordBot.Services; // Importante para LevelingService

var builder = Host.CreateApplicationBuilder(args);

// Configuración de Discord
var socketConfig = new DiscordSocketConfig
{
    // FIX: Agregado MessageContent y GuildMessages explícitamente
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

// Base de Datos
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite("Data Source=bagd_jam.db")
);

// Servicios
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LevelingService>(); // Singleton para estado de voz
builder.Services.AddHostedService<BotWorker>();

var host = builder.Build();

// Migración Automática
using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

await host.RunAsync();
