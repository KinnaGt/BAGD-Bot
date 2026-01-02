using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;
using MyDiscordBot.Services;

namespace MyDiscordBot.Modules;

public class LevelModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly LevelingService _levelingService;

    public LevelModule(IDbContextFactory<AppDbContext> dbFactory, LevelingService levelingService)
    {
        _dbFactory = dbFactory;
        _levelingService = levelingService;
    }

    [SlashCommand("rank", "Muestra tu nivel actual y XP")]
    public async Task CheckRank([Summary("usuario", "Opcional")] IUser? target = null)
    {
        var user = target ?? Context.User;

        using var db = await _dbFactory.CreateDbContextAsync();
        var stats = await db.UserLevels.FirstOrDefaultAsync(x =>
            x.UserId == user.Id && x.GuildId == Context.Guild.Id
        );

        if (stats == null)
        {
            await RespondAsync(
                $"{user.Username} aún no tiene experiencia registrada.",
                ephemeral: true
            );
            return;
        }

        int nextXp = _levelingService.GetNextLevelXp(stats.Level);
        string title = _levelingService.GetTitle(stats.Level);

        // Barra de progreso visual
        double progress = Math.Clamp((double)stats.XP / nextXp, 0, 1);
        int blocks = (int)(progress * 10);

        // FIX: Usamos Enumerable.Repeat porque los emojis son strings, no chars
        string filled = string.Concat(Enumerable.Repeat("🟩", blocks));
        string empty = string.Concat(Enumerable.Repeat("⬛", 10 - blocks));
        string progressBar = filled + empty;

        var embed = new EmbedBuilder()
            .WithAuthor(user)
            .WithTitle($"Creador de jueguitos - Nivel {stats.Level}")
            .WithDescription($"**Rango:** {title}\n\n**XP:** {stats.XP} / {nextXp}\n{progressBar}")
            .WithColor(Color.Purple)
            .Build();

        await RespondAsync(embed: embed);
    }

    [SlashCommand("leaderboard", "Top 10 usuarios con más experiencia")]
    public async Task Leaderboard()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var topUsers = await db
            .UserLevels.Where(x => x.GuildId == Context.Guild.Id)
            .OrderByDescending(x => x.XP)
            .Take(10)
            .ToListAsync();

        if (!topUsers.Any())
        {
            await RespondAsync("Aún no hay datos en el leaderboard.", ephemeral: true);
            return;
        }

        var description = "";
        int pos = 1;
        foreach (var stat in topUsers)
        {
            string medal = pos switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{pos}"
            };
            // Obtenemos el título desde el servicio para mostrarlo
            string title = _levelingService.GetTitle(stat.Level);
            description +=
                $"{medal} <@{stat.UserId}> - **Lvl {stat.Level}** ({title}) - {stat.XP} XP\n";
            pos++;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🏆 Tabla de usuarios Bagdad ")
            .WithDescription(description)
            .WithColor(Color.Gold)
            .Build();

        await RespondAsync(embed: embed);
    }
}
