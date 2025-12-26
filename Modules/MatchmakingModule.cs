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

    [SlashCommand("armar_grupos", "Genera grupos, guarda en DB y exporta CSV + Markdown")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task Matchmaking()
    {
        // Validación defensiva extra
        if (Context.User is SocketGuildUser user && !user.GuildPermissions.Administrator)
        {
            await RespondAsync("⛔ Acceso denegado: Solo administradores.", ephemeral: true);
            return;
        }

        await DeferAsync();

        using var db = await _dbFactory.CreateDbContextAsync();

        // 1. Limpieza inicial: Resetear asignaciones previas
        var allUsers = await db.Registrations.ToListAsync();
        allUsers.ForEach(u => u.GroupId = null);

        var soloUsers = allUsers.Where(u => u.TipoParticipacion.Contains("Solo")).ToList();

        if (soloUsers.Count == 0)
        {
            await FollowupAsync("⚠️ No hay usuarios inscritos como 'Solo'.");
            return;
        }

        // Lógica de separación (Full Time vs Resto)
        var fullTimeUsers = soloUsers
            .Where(u => u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var restrictedUsers = soloUsers
            .Where(u => !u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var buckets = restrictedUsers.GroupBy(u => u.Disponibilidad).ToList();
        var usedFullTimeIds = new HashSet<int>();

        int globalGroupCounter = 1;

        // Builders para los archivos de salida
        var csvBuilder = new StringBuilder();
        var mdBuilder = new StringBuilder();

        // Cabeceras
        csvBuilder.AppendLine(
            "GroupId,DiscordUsername,DiscordUserId,Roles,Experiencia,Disponibilidad"
        );

        mdBuilder.AppendLine($"# REPORTE DE MATCHMAKING - {DateTime.Now:g}");
        mdBuilder.AppendLine($"Total Candidatos: {soloUsers.Count}");
        mdBuilder.AppendLine($"- Restringidos: {restrictedUsers.Count}");
        mdBuilder.AppendLine($"- Full Time (Comodines): {fullTimeUsers.Count}\n");

        // --- PROCESAMIENTO ---

        // A. Procesar Franjas Horarias
        foreach (var bucket in buckets)
        {
            var availableFullTime = fullTimeUsers
                .Where(u => !usedFullTimeIds.Contains(u.Id))
                .ToList();
            var pool = bucket.ToList();
            pool.AddRange(availableFullTime);

            mdBuilder.AppendLine($"## 🕒 Franja: {bucket.Key}");
            mdBuilder.AppendLine(
                $"   Base: {bucket.Count()} | Comodines Disp: {availableFullTime.Count} | Total Pool: {pool.Count}"
            );

            var groups = BuildGroupsForBucket(pool);

            foreach (var group in groups)
            {
                // Asignar ID Global y Marcar Full Times usados
                foreach (var member in group)
                {
                    member.GroupId = globalGroupCounter;
                    if (
                        member.Disponibilidad.Equals(
                            "Full Time",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        usedFullTimeIds.Add(member.Id);
                }

                // Generar reporte MD para este grupo
                mdBuilder.AppendLine(FormatGroup(group, globalGroupCounter));

                globalGroupCounter++;
            }
            mdBuilder.AppendLine("--------------------------------------------------");
        }

        // B. Procesar Sobrantes Full Time
        var remainingFullTime = fullTimeUsers.Where(u => !usedFullTimeIds.Contains(u.Id)).ToList();
        if (remainingFullTime.Count > 0)
        {
            mdBuilder.AppendLine($"## 🕒 Franja: Full Time (Sobrantes)");
            mdBuilder.AppendLine($"   Cantidad: {remainingFullTime.Count}");

            var ftGroups = BuildGroupsForBucket(remainingFullTime);
            foreach (var group in ftGroups)
            {
                foreach (var member in group)
                    member.GroupId = globalGroupCounter;

                mdBuilder.AppendLine(FormatGroup(group, globalGroupCounter));
                globalGroupCounter++;
            }
        }
        else
        {
            mdBuilder.AppendLine(
                "## ℹ️ Todos los usuarios Full Time fueron asignados a otros horarios."
            );
        }

        // 2. GUARDAR CAMBIOS EN DB (Persistencia)
        await db.SaveChangesAsync();

        // 3. GENERAR CSV (Iteramos sobre los usuarios ya asignados y guardados)
        // Usamos los datos en memoria 'allUsers' que ya tienen el GroupId actualizado
        var assignedUsers = allUsers.Where(u => u.GroupId != null).OrderBy(u => u.GroupId).ToList();
        foreach (var u in assignedUsers)
        {
            var cleanRoles = u.Roles.Replace(",", "/").Replace("\n", " ");
            csvBuilder.AppendLine(
                $"{u.GroupId},{u.DiscordUsername},{u.DiscordUserId},{cleanRoles},{u.Experiencia},{u.Disponibilidad}"
            );
        }

        // 4. ENVIAR ARCHIVOS
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvBuilder.ToString()));
        using var mdStream = new MemoryStream(Encoding.UTF8.GetBytes(mdBuilder.ToString()));

        var attachments = new List<FileAttachment>
        {
            new FileAttachment(csvStream, "matchmaking_final.csv"),
            new FileAttachment(mdStream, "grupos_generados.md")
        };

        await Context.Interaction.FollowupWithFilesAsync(
            attachments,
            text: $"✅ **Matchmaking Completado**\n- Total Grupos: {globalGroupCounter - 1}\n- Usuarios Asignados: {assignedUsers.Count}\n\nUsa `/generargrupos` para aplicar los cambios en el servidor."
        );
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

        while (usedIds.Count < pool.Count)
        {
            var currentGroup = new List<Registration>();

            TryAddMember(currentGroup, audioPool, usedIds);
            TryAddMember(currentGroup, progPool, usedIds);
            TryAddMember(currentGroup, artPool, usedIds);

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

        return RedistributeSmallGroups(groups);
    }

    private List<List<Registration>> RedistributeSmallGroups(List<List<Registration>> groups)
    {
        const int MIN_GROUP_SIZE = 3;
        var validGroups = groups.Where(g => g.Count >= MIN_GROUP_SIZE).ToList();
        var smallGroups = groups.Where(g => g.Count < MIN_GROUP_SIZE).ToList();

        if (validGroups.Count == 0)
            return groups;

        var orphans = smallGroups.SelectMany(g => g).ToList();
        int index = 0;
        foreach (var orphan in orphans)
        {
            validGroups[index].Add(orphan);
            index = (index + 1) % validGroups.Count;
        }

        return validGroups;
    }

    private int PrioritySort(Registration u) =>
        u.Disponibilidad.Equals("Full Time", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

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
