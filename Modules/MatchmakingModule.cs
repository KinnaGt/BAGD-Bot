using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;
using MyDiscordBot.Models;

namespace MyDiscordBot.Modules;

public class MatchmakingModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MatchmakingModule(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [SlashCommand("armar_grupos", "Genera grupos balanceados")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task Matchmaking()
    {
        if (Context.User is SocketGuildUser user && !user.GuildPermissions.Administrator)
        {
            await RespondAsync("⛔ Acceso denegado: Solo administradores.", ephemeral: true);
            return;
        }

        await DeferAsync();

        using var db = await _dbFactory.CreateDbContextAsync();

        var soloUsers = await db
            .Registrations.Where(u => u.TipoParticipacion.Contains("Solo"))
            .ToListAsync();

        if (soloUsers.Count == 0)
        {
            await FollowupAsync("⚠️ No hay usuarios inscritos como 'Solo'.");
            return;
        }

        // 1. Separar Full Time (Comodines) de los Restringidos
        var fullTimeUsers = soloUsers
            .Where(u => u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var restrictedUsers = soloUsers
            .Where(u => !u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 2. Agrupar restringidos por su franja
        var buckets = restrictedUsers.GroupBy(u => u.Disponibilidad).ToList();
        var usedFullTimeIds = new HashSet<int>();

        var sb = new StringBuilder();
        sb.AppendLine($"# REPORTE DE MATCHMAKING - {DateTime.Now:g}");
        sb.AppendLine($"Total Candidatos: {soloUsers.Count}");
        sb.AppendLine($"- Restringidos: {restrictedUsers.Count}");
        sb.AppendLine($"- Full Time (Comodines): {fullTimeUsers.Count}\n");

        int globalGroupCounter = 1; // Contador global para numerar grupos

        // 3. Procesar Franjas Específicas
        foreach (var bucket in buckets)
        {
            // Pool = Gente de esta franja + Full Times LIBRES
            var availableFullTime = fullTimeUsers
                .Where(u => !usedFullTimeIds.Contains(u.Id))
                .ToList();
            var pool = bucket.ToList();
            pool.AddRange(availableFullTime);

            sb.AppendLine($"## 🕒 Franja: {bucket.Key}");
            sb.AppendLine(
                $"   Base: {bucket.Count()} | Comodines Disp: {availableFullTime.Count} | Total Pool: {pool.Count}"
            );

            var groups = BuildGroupsForBucket(pool);

            foreach (var group in groups)
            {
                foreach (var member in group)
                {
                    if (
                        member.Disponibilidad.Equals(
                            "Full Time",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        usedFullTimeIds.Add(member.Id);
                    }
                }
                sb.AppendLine(FormatGroup(group, globalGroupCounter++));
            }
            sb.AppendLine("--------------------------------------------------");
        }

        // 4. Procesar Full Times Sobrantes (Remanentes)
        var remainingFullTime = fullTimeUsers.Where(u => !usedFullTimeIds.Contains(u.Id)).ToList();
        if (remainingFullTime.Count > 0)
        {
            sb.AppendLine($"## 🕒 Franja: Full Time (Sobrantes)");
            sb.AppendLine($"   Cantidad: {remainingFullTime.Count}");

            var ftGroups = BuildGroupsForBucket(remainingFullTime);
            foreach (var group in ftGroups)
            {
                sb.AppendLine(FormatGroup(group, globalGroupCounter++));
            }
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await Context.Channel.SendFileAsync(
            stream,
            "grupos_generados.md",
            "✅ Matchmaking completado."
        );
        await FollowupAsync("Proceso finalizado.");
    }

    // --- ALGORITMO CORE ---

    private List<List<Registration>> BuildGroupsForBucket(List<Registration> pool)
    {
        var groups = new List<List<Registration>>();
        var usedIds = new HashSet<int>();
        var rnd = new Random();

        var audioPool = pool.OrderBy(PrioritySort)
            .ThenBy(_ => rnd.Next())
            .Where(u => u.Roles.Contains("Audio"))
            .ToList();
        var artPool = pool.OrderBy(PrioritySort)
            .ThenBy(_ => rnd.Next())
            .Where(u => u.Roles.Contains("Arte"))
            .ToList();
        var progPool = pool.OrderBy(PrioritySort)
            .ThenBy(_ => rnd.Next())
            .Where(u => u.Roles.Contains("Programador"))
            .ToList();

        var allPool = pool.OrderBy(PrioritySort).ThenBy(_ => rnd.Next()).ToList();

        int targetSize = 5;

        // FASE 1: Construcción Greedy
        while (usedIds.Count < pool.Count)
        {
            var currentGroup = new List<Registration>();

            // A. PRIORIDAD ROLES CRÍTICOS
            TryAddMember(currentGroup, audioPool, usedIds);
            TryAddMember(currentGroup, progPool, usedIds);
            TryAddMember(currentGroup, artPool, usedIds);

            // B. BALANCEO DE EXPERIENCIA
            double avgExp = currentGroup.Count > 0 ? currentGroup.Average(GetExpScore) : 0;

            if (avgExp > 0 && avgExp < 1.5 && currentGroup.Count < targetSize)
            {
                var veteran = allPool.FirstOrDefault(u =>
                    !usedIds.Contains(u.Id) && GetExpScore(u) >= 2
                );
                if (veteran != null)
                {
                    currentGroup.Add(veteran);
                    usedIds.Add(veteran.Id);
                }
            }
            else if (avgExp > 2.5 && currentGroup.Count < targetSize)
            {
                var rookie = allPool.FirstOrDefault(u =>
                    !usedIds.Contains(u.Id) && GetExpScore(u) == 1
                );
                if (rookie != null)
                {
                    currentGroup.Add(rookie);
                    usedIds.Add(rookie.Id);
                }
            }

            // C. RELLENO (FILL)
            while (currentGroup.Count < targetSize)
            {
                var nextUser = allPool.FirstOrDefault(u => !usedIds.Contains(u.Id));
                if (nextUser == null)
                    break;
                currentGroup.Add(nextUser);
                usedIds.Add(nextUser.Id);
            }

            if (currentGroup.Count > 0)
                groups.Add(currentGroup);
            else
                break;
        }

        // FASE 2: Redistribución de Grupos Pequeños (< 3 miembros)
        return RedistributeSmallGroups(groups);
    }

    private List<List<Registration>> RedistributeSmallGroups(List<List<Registration>> groups)
    {
        // Mínimo viable para una Jam: 3 personas (Prog + Arte + Audio/Design)
        const int MIN_GROUP_SIZE = 3;

        var validGroups = groups.Where(g => g.Count >= MIN_GROUP_SIZE).ToList();
        var smallGroups = groups.Where(g => g.Count < MIN_GROUP_SIZE).ToList();

        // Si no hay grupos válidos donde redistribuir, devolvemos lo que hay (mejor 2 personas que nada)
        if (validGroups.Count == 0)
            return groups;

        // Aplanar los miembros de los grupos pequeños
        var orphans = smallGroups.SelectMany(g => g).ToList();

        // Repartir huérfanos equitativamente (Round Robin)
        int index = 0;
        foreach (var orphan in orphans)
        {
            validGroups[index].Add(orphan);
            index = (index + 1) % validGroups.Count; // Ciclar
        }

        return validGroups;
    }

    private int PrioritySort(Registration u)
    {
        return u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private void TryAddMember(
        List<Registration> group,
        List<Registration> specificPool,
        HashSet<int> usedIds
    )
    {
        var candidate = specificPool.FirstOrDefault(u => !usedIds.Contains(u.Id));
        if (candidate != null)
        {
            group.Add(candidate);
            usedIds.Add(candidate.Id);
        }
    }

    private int GetExpScore(Registration r)
    {
        if (r.Experiencia.Contains("Veterano"))
            return 3;
        if (r.Experiencia.Contains("Intermedio"))
            return 2;
        return 1;
    }

    private string FormatGroup(List<Registration> group, int groupNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"> **GRUPO #{groupNumber} ({group.Count} miembros)**");

        bool hasAudio = group.Any(u => u.Roles.Contains("Audio"));
        bool hasProg = group.Any(u => u.Roles.Contains("Programador"));
        bool hasArt = group.Any(u => u.Roles.Contains("Arte"));

        string warnings = "";
        if (!hasAudio)
            warnings += " [Falta Audio]";
        if (!hasProg)
            warnings += " [Falta Prog]";
        if (!hasArt)
            warnings += " [Falta Arte]";

        if (!string.IsNullOrEmpty(warnings))
            sb.AppendLine($"> ⚠️ *Advertencia: {warnings}*");

        foreach (var m in group)
        {
            string icon = m.Experiencia.Contains("Veterano")
                ? "🔥"
                : (m.Experiencia.Contains("Principiante") ? "🌱" : "🔹");
            string ftTag = m.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase)
                ? " [FT]"
                : "";

            sb.AppendLine($"- {icon} {m.DiscordUsername}{ftTag} | {m.Roles} | {m.Experiencia}");
        }
        return sb.ToString();
    }
}
