using Discord;
using Discord.Interactions;

namespace MyDiscordBot.Modules;

public class HelpModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("help", "Muestra la lista de comandos disponibles y su funcionamiento")]
    public async Task HelpCommand()
    {
        var embed = new EmbedBuilder()
            .WithTitle("📖 Manual de Ayuda - BAGD Bot")
            .WithDescription(
                "Aquí tienes la lista de comandos disponibles para la gestión de la Global Game Jam y herramientas de la comunidad."
            )
            .WithColor(Color.Blue)
            .WithThumbnailUrl(
                Context.Client.CurrentUser.GetAvatarUrl()
                    ?? Context.Client.CurrentUser.GetDefaultAvatarUrl()
            )
            .AddField(
                "📝 /inscribirse",
                "Inicia el asistente interactivo para inscribirte en la **Global Game Jam 2026**.\n"
                    + "• Te pedirá datos personales, experiencia y preferencias.\n"
                    + "• Al finalizar, te asignará automáticamente el rol de participante."
            )
            .AddField(
                "📣 /jere",
                "*(Requiere Admin)* Spamea 'PAAAAA' en el canal actual para llamar la atención o celebrar."
            )
            // Pie de página
            .WithFooter("Buenos Aires Game Devs • GGJ 2026")
            .WithCurrentTimestamp()
            .Build();

        // Ephemeral: true para no spamear el chat si alguien pide ayuda
        await RespondAsync(embed: embed, ephemeral: true);
    }
}
