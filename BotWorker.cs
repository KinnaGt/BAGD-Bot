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
        // 1. Obtener Token
        // Prioridad: Variable de entorno > appsettings.Development.json > appsettings.json
        var token = _config["Discord:Token"];
        if (string.IsNullOrEmpty(token))
            throw new Exception("❌ Token no configurado. Revisa appsettings.Development.json");

        // 2. Cargar Módulos (IMPORTANTE: Hacerlo aquí, no en OnReady)
        // Al hacerlo en StartAsync, aseguramos que se carguen solo una vez.
        // Si lo pones en OnReady, al reconectarse el bot, intentará cargarlos de nuevo y fallará.
        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        _logger.LogInformation("✅ Módulos de comandos cargados en memoria.");

        // 3. Hooks de eventos
        _client.Log += LogAsync;
        _interactions.Log += LogAsync;
        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += HandleInteraction;

        // 4. Login y Conexión
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
    }

    private async Task OnReadyAsync()
    {
        // ID del servidor de pruebas para desarrollo rápido
        var guildIdStr = _config["Discord:TestGuildId"];

        if (ulong.TryParse(guildIdStr, out ulong guildId))
        {
            // Registro INSTANTÁNEO (Solo en este servidor)
            await _interactions.RegisterCommandsToGuildAsync(guildId);
            _logger.LogInformation($"🚀 Comandos registrados EXITOSAMENTE en Guild ID: {guildId}");
        }
        else
        {
            // Registro GLOBAL (Tarda ~1 hora en propagarse)
            _logger.LogWarning(
                "⚠️ 'TestGuildId' no encontrado o inválido. Usando registro GLOBAL (Lento: ~1 hora)."
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

            // Intentar notificar al usuario si el comando falló
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                var msg = "Hubo un error interno al ejecutar el comando.";
                if (interaction.HasResponded)
                    await interaction.FollowupAsync(msg, ephemeral: true);
                else
                    await interaction.RespondAsync(msg, ephemeral: true);
            }
        }
    }

    private Task LogAsync(LogMessage log)
    {
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
