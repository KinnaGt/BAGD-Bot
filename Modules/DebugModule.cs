using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;
using MyDiscordBot.Models;

namespace MyDiscordBot.Modules;

public class DebugModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DebugModule(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [SlashCommand("seed_db", "ADMIN: Genera 300 usuarios falsos para testear matchmaking")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task SeedDatabase()
    {
        await DeferAsync();

        var random = new Random();
        var fakeUsers = new List<Registration>();

        // Distribución ponderada realista para una Game Jam
        string[] experiencias =
        {
            "Principiante",
            "Principiante",
            "Principiante",
            "Principiante",
            "Principiante",
            "Intermedio",
            "Intermedio",
            "Intermedio",
            "Veterano"
        }; // 50% Jr
        string[] horarios =
        {
            "Noche",
            "Noche",
            "Noche",
            "Finde",
            "Finde",
            "Tarde",
            "Mañana",
            "Full Time"
        }; // Noche/Finde predominante

        // Roles (Permitimos multiclase, pero con peso)
        string[] rolesBase =
        {
            "Programador",
            "Programador",
            "Programador",
            "Programador",
            "Arte 2D",
            "Arte 2D",
            "Arte 3D",
            "Game Design",
            "Narrativa",
            "Audio",
            "Produccion"
        };

        for (int i = 0; i < 300; i++)
        {
            // Generar roles (1 a 2 roles por persona)
            int roleCount = random.Next(1, 3);
            var userRoles = new HashSet<string>();
            for (int r = 0; r < roleCount; r++)
            {
                userRoles.Add(rolesBase[random.Next(rolesBase.Length)]);
            }

            // Forzar escasez de Audio (10% chance real si no salió antes)
            if (random.NextDouble() < 0.1)
                userRoles.Add("Audio");

            var reg = new Registration
            {
                DiscordUserId = (ulong)random.NextInt64(10000000000000000, 90000000000000000),
                DiscordUsername = $"User_{i}",
                Nombre = $"MockName_{i}",
                Apellido = $"MockLastName_{i}",
                Edad = random.Next(18, 45).ToString(),
                Ubicacion = "MockCity",

                Experiencia = experiencias[random.Next(experiencias.Length)],
                Roles = string.Join(", ", userRoles),
                TipoParticipacion = "Solo", // Matchmaking es para gente sola
                Disponibilidad = horarios[random.Next(horarios.Length)],
                ConsentimientoDifusion = true,
                NecesidadesEspeciales = "Ninguna"
            };
            fakeUsers.Add(reg);
        }

        using var db = await _dbFactory.CreateDbContextAsync();

        // Limpiar tabla anterior para test limpio
        db.Registrations.RemoveRange(db.Registrations);
        await db.SaveChangesAsync();

        await db.Registrations.AddRangeAsync(fakeUsers);
        await db.SaveChangesAsync();

        await FollowupAsync($"✅ Base de datos poblada con {fakeUsers.Count} usuarios simulados.");
    }
}
