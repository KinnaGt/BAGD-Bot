using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;

namespace MyDiscordBot.Modules;

public class GroupManagerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public GroupManagerModule(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [SlashCommand("generargrupos", "Crea canales y roles basados en la DB")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task GenerateGroupsInfrastructure()
    {
        await DeferAsync();

        using var db = await _dbFactory.CreateDbContextAsync();
        var guild = Context.Guild;

        var assignedUsers = await db
            .Registrations.Where(r => r.GroupId != null)
            .OrderBy(r => r.GroupId)
            .ToListAsync();

        if (assignedUsers.Count == 0)
        {
            await FollowupAsync(
                "⚠️ No hay usuarios con grupo asignado en la DB. Ejecuta /armar_grupos primero."
            );
            return;
        }

        var groups = assignedUsers.GroupBy(u => u.GroupId!.Value).ToList();

        await FollowupAsync(
            $"🚀 Iniciando creación de infraestructura para {groups.Count} grupos..."
        );

        var categoryName = "GGJ 2026 TEAMS";
        // Usamos ICategoryChannel para compatibilidad (Socket/Rest)
        ICategoryChannel? category = guild.CategoryChannels.FirstOrDefault(c =>
            c.Name == categoryName
        );

        if (category == null)
        {
            category = await guild.CreateCategoryChannelAsync(categoryName);
        }

        int successCount = 0;
        int errorCount = 0;

        foreach (var group in groups)
        {
            int groupId = group.Key;
            string roleName = $"Grupo #{groupId}";
            string channelName = $"grupo-{groupId}";

            try
            {
                IRole? role = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                if (role == null)
                {
                    role = await guild.CreateRoleAsync(roleName, null, Color.Blue, isHoisted: true);
                }

                ITextChannel? channel = guild.TextChannels.FirstOrDefault(c =>
                    c.Name == channelName && c.CategoryId == category.Id
                );

                if (channel == null)
                {
                    var perms = new List<Overwrite>
                    {
                        new Overwrite(
                            guild.EveryoneRole.Id,
                            PermissionTarget.Role,
                            new OverwritePermissions(viewChannel: PermValue.Deny)
                        ),
                        new Overwrite(
                            role.Id,
                            PermissionTarget.Role,
                            new OverwritePermissions(
                                viewChannel: PermValue.Allow,
                                sendMessages: PermValue.Allow
                            )
                        )
                    };

                    channel = await guild.CreateTextChannelAsync(
                        channelName,
                        p =>
                        {
                            p.CategoryId = category.Id;
                            p.PermissionOverwrites = perms;
                        }
                    );

                    await channel.SendMessageAsync(
                        $"👋 ¡Bienvenidos al **{roleName}**! Este es su espacio privado de trabajo."
                    );
                }

                foreach (var memberReg in group)
                {
                    var discordUser = guild.GetUser(memberReg.DiscordUserId);
                    if (discordUser != null)
                    {
                        if (!discordUser.Roles.Any(r => r.Id == role.Id))
                        {
                            await discordUser.AddRoleAsync(role);
                        }
                    }
                }

                successCount++;
                await Task.Delay(1000); // Rate Limit Protection
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Grupo {groupId}: {ex.Message}");
                errorCount++;
            }
        }

        await Context.Channel.SendMessageAsync(
            $"✅ **Proceso Finalizado**\n- Grupos Creados: {successCount}\n- Errores: {errorCount}"
        );
    }

    [SlashCommand("borrargrupos", "PELIGRO: Elimina canales, categoría y roles creados")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task NukeGroups()
    {
        await DeferAsync();
        var guild = Context.Guild;
        var categoryName = "GGJ 2026 TEAMS";

        // 1. Borrar Canales y Categoría
        ICategoryChannel? category = guild.CategoryChannels.FirstOrDefault(c =>
            c.Name == categoryName
        );
        int deletedChannels = 0;

        if (category != null)
        {
            // Obtenemos los canales de la categoría de forma segura
            var channels = guild.TextChannels.Where(c => c.CategoryId == category.Id).ToList();
            foreach (var c in channels)
            {
                await c.DeleteAsync();
                deletedChannels++;
                await Task.Delay(500); // Rate limit guard
            }
            await category.DeleteAsync();
        }

        // 2. Borrar Roles
        // Filtramos roles generados por el bot (patrón "Grupo #")
        var roles = guild.Roles.Where(r => r.Name.StartsWith("Grupo #")).ToList();
        int deletedRoles = 0;
        foreach (var r in roles)
        {
            await r.DeleteAsync();
            deletedRoles++;
            await Task.Delay(500);
        }

        // 3. Limpiar DB (Opcional, pero recomendado para mantener consistencia)
        using var db = await _dbFactory.CreateDbContextAsync();
        var usersWithGroup = await db.Registrations.Where(r => r.GroupId != null).ToListAsync();
        foreach (var u in usersWithGroup)
            u.GroupId = null;
        await db.SaveChangesAsync();

        await FollowupAsync(
            $"💥 **Destrucción Completada**\n- Canales eliminados: {deletedChannels}\n- Roles eliminados: {deletedRoles}\n- Asignaciones en DB reseteadas."
        );
    }
}
