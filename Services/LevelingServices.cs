using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;
using MyDiscordBot.Models;

namespace MyDiscordBot.Services;

public class LevelingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<LevelingService> _logger; // Logger inyectado

    // Configuración
    private const int XP_PER_MESSAGE = 20;
    private const int MESSAGE_COOLDOWN_SECONDS = 60;
    private const int XP_PER_MINUTE_VOICE = 5;

    private readonly ConcurrentDictionary<ulong, DateTime> _voiceSessions = new();

    private static readonly string[] _titles =
    {
        "Hello Worlder", // Lvl 0
        "Script Kiddie", // Lvl 1
        "Intern Debugger", // Lvl 2
        "Asset Flipper", // Lvl 3
        "Spaghetti Chef", // Lvl 4
        "Indie Developer", // Lvl 5
        "Senior Developer", // Lvl 6
        "Tech Artist", // Lvl 7
        "System Architect", // Lvl 8
        "Game Director", // Lvl 9
        "Godot Evangelist" // Lvl 10
    };

    public LevelingService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<LevelingService> logger
    )
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    // --- MESSAGING LOGIC ---

    public async Task ProcessMessageAsync(SocketMessage msg)
    {
        if (msg.Author.IsBot || msg.Channel is IDMChannel)
            return;

        var user = msg.Author as SocketGuildUser;
        if (user == null)
        {
            _logger.LogWarning(
                $"[XP-FAIL] El autor '{msg.Author.Username}' es null al castear. ¿Están activados los Intents 'GuildMembers' y 'Guilds'?"
            );
            return;
        }

        using var db = await _dbFactory.CreateDbContextAsync();

        var stats = await db.UserLevels.FirstOrDefaultAsync(x =>
            x.UserId == user.Id && x.GuildId == user.Guild.Id
        );

        if (stats == null)
        {
            // Crea el registro para CUALQUIER usuario, sin importar si está inscrito en la Jam
            stats = new UserLevel { UserId = user.Id, GuildId = user.Guild.Id };
            db.UserLevels.Add(stats);
            _logger.LogInformation(
                $"[XP-NEW] Usuario añadido al sistema de niveles: {user.Username}"
            );
        }

        // Anti-Spam Check
        double secondsSinceLast = (DateTime.Now - stats.LastMessageDate).TotalSeconds;
        if (secondsSinceLast < MESSAGE_COOLDOWN_SECONDS)
        {
            // Log de depuración para saber que el sistema funciona pero está bloqueando por spam
            _logger.LogDebug(
                $"[XP-COOLDOWN] {user.Username} ignorado. Faltan {MESSAGE_COOLDOWN_SECONDS - secondsSinceLast:F1}s para sumar XP."
            );
            return;
        }

        stats.LastMessageDate = DateTime.Now;
        await AddXpInternalAsync(stats, XP_PER_MESSAGE, msg.Channel, user.Username, "MSG");

        await db.SaveChangesAsync();
    }

    // --- VOICE LOGIC ---

    public async Task ProcessVoiceStateAsync(
        SocketUser user,
        SocketVoiceState oldState,
        SocketVoiceState newState
    )
    {
        if (user.IsBot)
            return;

        // 1. Entra a canal
        if (oldState.VoiceChannel == null && newState.VoiceChannel != null)
        {
            if (newState.IsSelfDeafened || newState.IsSelfMuted)
                return;

            _voiceSessions.TryAdd(user.Id, DateTime.Now);
            _logger.LogDebug($"[VOICE-START] {user.Username} inició sesión de voz.");
        }
        // 2. Sale de canal
        else if (oldState.VoiceChannel != null && newState.VoiceChannel == null)
        {
            await FinalizeVoiceSessionAsync(user.Id, (SocketGuildUser)user);
        }
        // 3. Cambia estado (Mute/Deafen)
        else if (oldState.VoiceChannel != null && newState.VoiceChannel != null)
        {
            if (newState.IsSelfDeafened || newState.IsSelfMuted)
            {
                await FinalizeVoiceSessionAsync(user.Id, (SocketGuildUser)user);
            }
            else if (
                (oldState.IsSelfDeafened || oldState.IsSelfMuted)
                && (!newState.IsSelfDeafened && !newState.IsSelfMuted)
            )
            {
                _voiceSessions.TryAdd(user.Id, DateTime.Now);
                _logger.LogDebug($"[VOICE-RESUME] {user.Username} reactivó audio.");
            }
        }
    }

    private async Task FinalizeVoiceSessionAsync(ulong userId, SocketGuildUser guildUser)
    {
        if (_voiceSessions.TryRemove(userId, out DateTime joinTime))
        {
            var duration = DateTime.Now - joinTime;
            int minutes = (int)duration.TotalMinutes;

            if (minutes > 0)
            {
                int xpEarned = minutes * XP_PER_MINUTE_VOICE;

                using var db = await _dbFactory.CreateDbContextAsync();
                var stats = await db.UserLevels.FirstOrDefaultAsync(x =>
                    x.UserId == userId && x.GuildId == guildUser.Guild.Id
                );

                if (stats == null)
                {
                    stats = new UserLevel { UserId = userId, GuildId = guildUser.Guild.Id };
                    db.UserLevels.Add(stats);
                    _logger.LogInformation(
                        $"[XP-NEW] Usuario añadido al sistema de niveles (Voz): {guildUser.Username}"
                    );
                }

                await AddXpInternalAsync(stats, xpEarned, null, guildUser.Username, "VOICE");
                await db.SaveChangesAsync();
            }
            else
            {
                _logger.LogDebug($"[VOICE-SHORT] {guildUser.Username} estuvo menos de 1 minuto.");
            }
        }
    }

    // --- CORE LOGIC ---

    private async Task AddXpInternalAsync(
        UserLevel stats,
        int amount,
        ISocketMessageChannel? channel,
        string username,
        string source
    )
    {
        int oldLevel = stats.Level;
        stats.XP += amount;

        // Log claro en consola
        _logger.LogInformation(
            $"[XP-{source}] {username}: +{amount} XP | Total: {stats.XP} | Lvl: {stats.Level}"
        );

        // Fórmula de nivel
        int nextLevelXp = 100 * (int)Math.Pow(stats.Level + 1, 2);

        if (stats.XP >= nextLevelXp)
        {
            stats.Level++;
            if (stats.Level >= _titles.Length)
                stats.Level = _titles.Length - 1;

            string newTitle = _titles[stats.Level];

            _logger.LogInformation(
                $"[LEVEL-UP] 🎉 {username} subió a nivel {stats.Level} ({newTitle})"
            );

            if (channel != null)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🎉 ¡LEVEL UP!")
                    .WithDescription(
                        $"Has subido al **Nivel {stats.Level}**.\nNuevo Rango: **{newTitle}** 👾"
                    )
                    .WithColor(Color.Gold)
                    .Build();

                await channel.SendMessageAsync(embed: embed);
            }
        }
    }

    public string GetTitle(int level)
    {
        if (level >= _titles.Length)
            return _titles.Last();
        return _titles[level];
    }

    public int GetNextLevelXp(int currentLevel) => 100 * (int)Math.Pow(currentLevel + 1, 2);
}
