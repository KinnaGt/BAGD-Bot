using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MyDiscordBot.Data;
using MyDiscordBot.Models;

namespace MyDiscordBot.Modules;

public class RegistrationModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private const string JAM_ROLE_NAME = "Global game jam 2026";

    public RegistrationModule(IDbContextFactory<AppDbContext> dbFactory, IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    // --- INICIO ---

    [SlashCommand("inscribirse", "Inicia la inscripción a la GGJ 2026")]
    public async Task StartRegistration()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.Registrations.AnyAsync(r => r.DiscordUserId == Context.User.Id))
        {
            await RespondAsync(
                "⚠️ Ya estás inscrito. Usa /reset_test si necesitas empezar de cero.",
                ephemeral: true
            );
            return;
        }

        // Paso 1: Identidad (Texto -> Modal es lo mejor para esto)
        await RespondWithModalAsync<IdentityModal>("reg_modal_identity");
    }

    [SlashCommand("reset_test", "ADMIN: Borra registro local para testear")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ResetTest()
    {
        await DeferAsync(ephemeral: true);
        _cache.Remove($"reg_{Context.User.Id}");
        using var db = await _dbFactory.CreateDbContextAsync();
        var rec = await db.Registrations.FirstOrDefaultAsync(r =>
            r.DiscordUserId == Context.User.Id
        );
        if (rec != null)
        {
            db.Registrations.Remove(rec);
            await db.SaveChangesAsync();
            await FollowupAsync("✅ DB Limpia.");
        }
        else
        {
            await FollowupAsync("ℹ️ No había registro en DB.");
        }
    }

    // --- PASO 1: IDENTIDAD (MODAL) ---

    [ModalInteraction("reg_modal_identity")]
    public async Task OnIdentitySubmit(IdentityModal modal)
    {
        var data = new Registration
        {
            DiscordUserId = Context.User.Id,
            DiscordUsername = Context.User.Username,
            Nombre = modal.Nombre,
            Apellido = modal.Apellido,
            DNI = modal.Dni,
            Edad = modal.Edad,
            Ubicacion = modal.Location
        };

        _cache.Set($"reg_{Context.User.Id}", data, TimeSpan.FromMinutes(30));

        // Construimos el UI del Paso 2 (Select Menus)
        var embed = new EmbedBuilder()
            .WithTitle("Paso 2/4: Perfil Profesional")
            .WithDescription(
                "Selecciona las opciones que mejor te describan. \n**Debes seleccionar algo en cada menú para continuar.**"
            )
            .WithColor(Color.Blue)
            .Build();

        var builder = new ComponentBuilder();

        // Menu 1: Experiencia
        var expMenu = new SelectMenuBuilder()
            .WithCustomId("reg_sel_exp")
            .WithPlaceholder("Nivel de Experiencia")
            .AddOption("Principiante (Primera Jam)", "Principiante")
            .AddOption("Intermedio (Algunas Jams/Proyectos)", "Intermedio")
            .AddOption("Veterano (Profesional/Muchas Jams)", "Veterano");

        // Menu 2: Roles (Multi-select)
        var rolesMenu = new SelectMenuBuilder()
            .WithCustomId("reg_sel_roles")
            .WithPlaceholder("Roles que ocuparás (Max 3)")
            .WithMinValues(1)
            .WithMaxValues(3)
            .AddOption("Programación", "Programador")
            .AddOption("Arte 2D", "Arte 2D")
            .AddOption("Arte 3D", "Arte 3D")
            .AddOption("Audio / Música", "Audio")
            .AddOption("Game Design", "Game Design")
            .AddOption("Narrativa / Guión", "Narrativa")
            .AddOption("QA / Testing", "QA")
            .AddOption("Producción", "Produccion");

        // Menu 3: Grupo
        var groupMenu = new SelectMenuBuilder()
            .WithCustomId("reg_sel_group")
            .WithPlaceholder("¿Cómo participas?")
            .AddOption("Solo (Busco equipo o trabajo solo)", "Solo")
            .AddOption("Grupo Pre-armado (Ya tengo equipo)", "Grupo");

        // Botón Continuar
        var btn = new ButtonBuilder()
            .WithLabel("Confirmar y Seguir ➡️")
            .WithCustomId("reg_btn_step3")
            .WithStyle(ButtonStyle.Primary);

        builder.WithSelectMenu(expMenu);
        builder.WithSelectMenu(rolesMenu);
        builder.WithSelectMenu(groupMenu);
        builder.WithButton(btn);

        await RespondAsync(embed: embed, components: builder.Build(), ephemeral: true);
    }

    // --- HANDLERS DE SELECCIÓN PASO 2 (Guardado en Caché al vuelo) ---

    [ComponentInteraction("reg_sel_*")]
    public async Task OnSelection(string id, string[] selection)
    {
        // Recuperamos el objeto
        if (!_cache.TryGetValue($"reg_{Context.User.Id}", out Registration? data) || data == null)
        {
            await RespondAsync("❌ Sesión expirada.", ephemeral: true);
            return;
        }

        // Actualizamos campo según el ID del menú
        var value = string.Join(", ", selection);

        switch (id)
        {
            case "exp":
                data.Experiencia = value;
                break;
            case "roles":
                data.Roles = value;
                break;
            case "group":
                data.TipoParticipacion = value;
                break;
            case "avail":
                data.Disponibilidad = value;
                break; // Usado en paso 3
            case "consent":
                data.ConsentimientoDifusion = selection.Contains("SI");
                break; // Usado en paso 3
        }

        _cache.Set($"reg_{Context.User.Id}", data, TimeSpan.FromMinutes(30));

        // Es necesario responder a la interacción de Discord para que no de error
        await DeferAsync();
    }

    // --- PASO 3: LOGÍSTICA ---

    [ComponentInteraction("reg_btn_step3")]
    public async Task GoToStep3()
    {
        // Validación: ¿Eligió todo?
        if (!_cache.TryGetValue($"reg_{Context.User.Id}", out Registration? data) || data == null)
        {
            await RespondAsync("❌ Sesión expirada.", ephemeral: true);
            return;
        }

        if (
            string.IsNullOrEmpty(data.Experiencia)
            || string.IsNullOrEmpty(data.Roles)
            || string.IsNullOrEmpty(data.TipoParticipacion)
        )
        {
            await RespondAsync(
                "⚠️ **Falta información.** Por favor selecciona opciones en todos los menús de arriba antes de continuar.",
                ephemeral: true
            );
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Paso 3/4: Disponibilidad y Legal")
            .WithDescription("Definamos tus horarios y permisos.")
            .WithColor(Color.Blue)
            .Build();

        var builder = new ComponentBuilder();

        // Menu Disponibilidad
        var availMenu = new SelectMenuBuilder()
            .WithCustomId("reg_sel_avail")
            .WithPlaceholder("Franja Horaria y Disponibilidad")
            .AddOption("Full Time (Toda la semana)", "Full Time")
            .AddOption("Part Time - Mañana", "Mañana")
            .AddOption("Part Time - Tarde", "Tarde")
            .AddOption("Part Time - Noche", "Noche")
            .AddOption("Solo Fin de Semana", "Finde");

        // Menu Consentimiento (Simulando Checkbox)
        var consentMenu = new SelectMenuBuilder()
            .WithCustomId("reg_sel_consent")
            .WithPlaceholder("¿Autorizas difusión de tu juego/nombre?")
            .AddOption("✅ SÍ, Autorizo a BAGD", "SI")
            .AddOption("❌ NO Autorizo", "NO");

        var btn = new ButtonBuilder()
            .WithLabel("Casi listo... Último paso ➡️")
            .WithCustomId("reg_btn_step4")
            .WithStyle(ButtonStyle.Success);

        builder.WithSelectMenu(availMenu);
        builder.WithSelectMenu(consentMenu);
        builder.WithButton(btn);

        // Actualizamos el mensaje o mandamos uno nuevo? Mandamos nuevo para historial limpio
        await RespondAsync(embed: embed, components: builder.Build(), ephemeral: true);
    }

    // --- PASO 4: CIERRE (MODAL OPCIONAL) ---

    [ComponentInteraction("reg_btn_step4")]
    public async Task GoToFinalStep()
    {
        if (!_cache.TryGetValue($"reg_{Context.User.Id}", out Registration? data) || data == null)
        {
            await RespondAsync("❌ Sesión expirada.", ephemeral: true);
            return;
        }

        // Validación paso 3
        if (string.IsNullOrEmpty(data.Disponibilidad)) // Consentimiento es bool false por defecto, difícil validar si eligió NO o no eligió nada, asumimos NO default.
        {
            await RespondAsync("⚠️ Selecciona tu disponibilidad.", ephemeral: true);
            return;
        }

        // Abrimos el último modal para texto libre opcional
        await RespondWithModalAsync<SpecialNeedsModal>("reg_modal_final");
    }

    [ModalInteraction("reg_modal_final")]
    public async Task OnFinalSubmit(SpecialNeedsModal modal)
    {
        await DeferAsync(ephemeral: true);

        if (!_cache.TryGetValue($"reg_{Context.User.Id}", out Registration? data) || data == null)
        {
            await FollowupAsync("❌ Sesión expirada.");
            return;
        }

        data.NecesidadesEspeciales = modal.Special ?? "Ninguna";

        // GUARDADO FINAL
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            db.Registrations.Add(data);
            await db.SaveChangesAsync();
            _cache.Remove($"reg_{Context.User.Id}");
            Console.WriteLine($"[EXITO] Registro completo: {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR DB] {ex}");
            await FollowupAsync("❌ Error crítico guardando en base de datos.");
            return;
        }

        // Asignar Rol
        try
        {
            var user = Context.User as IGuildUser;
            var role = user?.Guild.Roles.FirstOrDefault(r =>
                r.Name.Equals(JAM_ROLE_NAME, StringComparison.OrdinalIgnoreCase)
            );
            if (role != null)
                await user!.AddRoleAsync(role);
        }
        catch
        { /* Ignorar error rol */
        }

        await FollowupAsync(
            embed: new EmbedBuilder()
                .WithTitle("🎉 ¡Inscripción Completa!")
                .WithDescription($"Bienvenido a la **{JAM_ROLE_NAME}**.")
                .WithColor(Color.Green)
                .Build()
        );
    }

    // --- MODALES DEFINITION ---

    public class IdentityModal : IModal
    {
        public string Title => "GGJ 2026 - Datos Personales";

        [InputLabel("Nombre")]
        [ModalTextInput("nombre", placeholder: "Nombre Real", minLength: 2)]
        public string Nombre { get; set; } = string.Empty;

        [InputLabel("Apellido")]
        [ModalTextInput("apellido", placeholder: "Apellido Real", minLength: 2)]
        public string Apellido { get; set; } = string.Empty;

        [InputLabel("DNI")]
        [ModalTextInput("dni", minLength: 6, maxLength: 10)]
        public string Dni { get; set; } = string.Empty;

        [InputLabel("Edad")]
        [ModalTextInput("edad", placeholder: "Ej: 24", maxLength: 3)]
        public string Edad { get; set; } = string.Empty;

        [InputLabel("Ciudad, Provincia, País")]
        [ModalTextInput("location")]
        public string Location { get; set; } = string.Empty;
    }

    public class SpecialNeedsModal : IModal
    {
        public string Title => "Necesidades Especiales";

        [InputLabel("Observaciones (Opcional)")]
        [RequiredInput(false)]
        [ModalTextInput(
            "special",
            TextInputStyle.Paragraph,
            placeholder: "Déjalo vacío si no aplica"
        )]
        public string? Special { get; set; }
    }
}
