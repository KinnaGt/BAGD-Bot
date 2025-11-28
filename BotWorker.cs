using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace MyDiscordBot;

public class BotWorker : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<BotWorker> _logger;

    public BotWorker(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IConfiguration config,
        ILogger<BotWorker> logger
    )
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = _config["Discord:Token"];
        if (string.IsNullOrEmpty(token))
            throw new Exception("Token no configurado en appsettings.json");

        _client.Log += LogAsync;
        _interactions.Log += LogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += HandleInteraction;

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
    }

    private async Task OnReadyAsync()
    {
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        var guildIdStr = _config["Discord:TestGuildId"];

        if (ulong.TryParse(guildIdStr, out ulong guildId))
        {
            await _interactions.RegisterCommandsToGuildAsync(guildId);
            _logger.LogInformation($"Comandos registrados en Guild ID: {guildId}");
        }
        else
        {
            _logger.LogWarning(
                "No se encontró TestGuildId en config. Registrando globalmente (lento)..."
            );
            await _interactions.RegisterCommandsGloballyAsync();
        }
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al ejecutar comando");
        }
    }

    private Task LogAsync(LogMessage log)
    {
        // Mapeo de severidad de log de Discord a Microsoft Extensions Logging
        switch (log.Severity)
        {
            case LogSeverity.Critical:
                _logger.LogCritical(log.Exception, log.Message);
                break;
            case LogSeverity.Error:
                _logger.LogError(log.Exception, log.Message);
                break;
            case LogSeverity.Warning:
                _logger.LogWarning(log.Exception, log.Message);
                break;
            default:
                _logger.LogInformation(log.Message);
                break;
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.StopAsync();
    }
}
