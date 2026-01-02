using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Data;
using MyDiscordBot.Models;

namespace MyDiscordBot.Modules;

public class DataManagerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DataManagerModule(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [SlashCommand("importar_csv", "ADMIN: Sobrescribe la DB con un archivo CSV adjunto")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ImportCsv(IAttachment archivo)
    {
        // 1. Validaciones básicas
        if (!archivo.Filename.EndsWith(".csv"))
        {
            await RespondAsync("❌ El archivo debe tener extensión .csv", ephemeral: true);
            return;
        }

        await DeferAsync();

        // 2. Descargar y leer el archivo
        using var client = new HttpClient();
        var content = await client.GetStringAsync(archivo.Url);

        // Normalizar saltos de línea y dividir
        var lines = content.Replace("\r\n", "\n").Split('\n');

        if (lines.Length < 2)
        {
            await FollowupAsync("❌ El archivo parece estar vacío o no tiene datos.");
            return;
        }

        // 3. Procesar Cabeceras para mapeo dinámico
        var headers = ParseCsvLine(lines[0]);
        var colMap = new Dictionary<string, int>();
        for (int i = 0; i < headers.Count; i++)
            colMap[headers[i].Trim().ToLower()] = i;

        // Validar columnas críticas según TU formato
        // GroupId,DiscordUsername,DiscordUserId,Roles,Experiencia,Disponibilidad
        string[] requiredCols = { "discorduserid", "roles", "experiencia", "disponibilidad" };

        foreach (var req in requiredCols)
        {
            if (!colMap.ContainsKey(req))
            {
                await FollowupAsync(
                    $"❌ Falta la columna obligatoria: **{req}**.\nCabeceras detectadas: {string.Join(", ", headers)}"
                );
                return;
            }
        }

        var newRegistrations = new List<Registration>();
        var errors = new List<string>();

        // 4. Parsear Filas
        for (int i = 1; i < lines.Length; i++) // Empezamos en 1 para saltar header
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var values = ParseCsvLine(line);

                // Validación básica de columnas
                if (values.Count < requiredCols.Length)
                    continue;

                var reg = new Registration
                {
                    RegistrationDate = DateTime.Now,

                    // Mapeo Directo (Obligatorios)
                    DiscordUserId = ulong.Parse(values[colMap["discorduserid"]]),

                    // Mapeo Opcional/Flexible
                    DiscordUsername = colMap.ContainsKey("discordusername")
                        ? values[colMap["discordusername"]]
                        : "Importado",
                    Roles = colMap.ContainsKey("roles") ? values[colMap["roles"]] : "",
                    Experiencia = colMap.ContainsKey("experiencia")
                        ? values[colMap["experiencia"]]
                        : "",
                    Disponibilidad = colMap.ContainsKey("disponibilidad")
                        ? values[colMap["disponibilidad"]]
                        : "",

                    // GroupId es clave para tu caso de uso
                    GroupId =
                        colMap.ContainsKey("groupid")
                        && int.TryParse(values[colMap["groupid"]], out int gid)
                            ? gid
                            : null,

                    // Campos Default (No vienen en tu CSV simplificado pero la DB los pide)
                    TipoParticipacion = colMap.ContainsKey("tipoparticipacion")
                        ? values[colMap["tipoparticipacion"]]
                        : "Importado/CSV",
                    Nombre = "",
                    Apellido = "",
                    DNI = "",
                    Edad = "",
                    Ubicacion = "",
                    ConsentimientoDifusion = true,
                    NecesidadesEspeciales = ""
                };

                newRegistrations.Add(reg);
            }
            catch (Exception ex)
            {
                errors.Add($"Línea {i + 1}: {ex.Message}");
            }
        }

        // 5. Transacción de Base de Datos
        using var db = await _dbFactory.CreateDbContextAsync();
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // A. Limpiar tabla actual (Sobreescritura total)
            db.Registrations.RemoveRange(db.Registrations);
            await db.SaveChangesAsync();

            // B. Insertar nuevos datos
            await db.Registrations.AddRangeAsync(newRegistrations);
            await db.SaveChangesAsync();

            // C. Commit
            await transaction.CommitAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"✅ **Importación Exitosa**");
            sb.AppendLine($"- Registros procesados: {newRegistrations.Count}");
            sb.AppendLine($"- Errores de formato: {errors.Count}");
            sb.AppendLine(
                $"\n💡 La base de datos ha sido actualizada. Si el CSV tenía `GroupId`, puedes ejecutar `/generargrupos` ahora mismo."
            );

            if (errors.Count > 0)
            {
                if (errors.Count <= 10)
                    sb.AppendLine("\n**Errores:**\n" + string.Join("\n", errors));
                else
                    sb.AppendLine("\n⚠️ Revisa el CSV, hay demasiados errores de formato.");
            }

            await FollowupAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            await FollowupAsync(
                $"💥 **Error Fatal en DB**: No se realizaron cambios.\n{ex.Message}"
            );
        }
    }

    // --- Parser CSV ---
    private List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var currentVal = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentVal.Append('"');
                    i++;
                }
                else
                    inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentVal.ToString());
                currentVal.Clear();
            }
            else
            {
                currentVal.Append(c);
            }
        }
        values.Add(currentVal.ToString());
        return values;
    }
}
