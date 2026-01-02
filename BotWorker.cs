using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MyDiscordBot.Services; // Necesario para LevelingService

namespace MyDiscordBot;

public class BotWorker : IHostedService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger<BotWorker> _logger;
    private readonly LevelingService _leveling; // <--- 1. FALTABA ESTO

    public BotWorker(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IConfiguration config,
        ILogger<BotWorker> logger,
        LevelingService leveling // <--- 2. INYECCIÓN
    )
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _config = config;
        _logger = logger;
        _leveling = leveling;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var token = _config["Discord:Token"];
        if (string.IsNullOrEmpty(token))
            throw new Exception("❌ Token no configurado.");

        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        _logger.LogInformation("✅ Módulos cargados.");

        _client.Log += LogAsync;
        _interactions.Log += LogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += HandleInteraction;

        // --- 3. SUSCRIPCIONES FALTANTES (CRÍTICO) ---
        // Sin esto, el bot ignora los mensajes y la voz para el XP
        _client.MessageReceived += OnMessageReceived;
        _client.UserVoiceStateUpdated += OnVoiceStateUpdated;
        // ---------------------------------------------

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
    }

    // --- 4. HANDLERS QUE FALTABAN ---
    private async Task OnMessageReceived(SocketMessage msg)
    {
        await _leveling.ProcessMessageAsync(msg);
    }

    private async Task OnVoiceStateUpdated(
        SocketUser user,
        SocketVoiceState oldState,
        SocketVoiceState newState
    )
    {
        await _leveling.ProcessVoiceStateAsync(user, oldState, newState);
    }

    // --------------------------------

    private async Task OnReadyAsync()
    {
        var guildIdStr = _config["Discord:TestGuildId"];
        if (ulong.TryParse(guildIdStr, out ulong guildId))
        {
            await _interactions.RegisterCommandsToGuildAsync(guildId);
            _logger.LogInformation($"🚀 Comandos registrados en: {guildId}");
        }
        else
        {
            _logger.LogWarning("⚠️ Usando registro GLOBAL (Lento).");
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
            _logger.LogError(ex, "Error ejecutando comando");
            if (interaction.Type == InteractionType.ApplicationCommand && !interaction.HasResponded)
                await interaction.RespondAsync("Error interno.", ephemeral: true);
        }
    }

    private Task LogAsync(LogMessage log)
    {
        // Mapeo simple de logs
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
