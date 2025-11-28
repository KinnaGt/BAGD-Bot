using Discord;
using Discord.Interactions;

namespace MyDiscordBot.Modules;

public class GeneralModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("jere", "Spamea PAAAAA en el canal actual")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task JereCommand()
    {
        await RespondAsync("PAAAAAA");
    }
}
