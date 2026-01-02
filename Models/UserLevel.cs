using System.ComponentModel.DataAnnotations;

namespace MyDiscordBot.Models;

public class UserLevel
{
    [Key]
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong GuildId { get; set; }

    public int XP { get; set; } = 0;
    public int Level { get; set; } = 0;

    public DateTime LastMessageDate { get; set; } = DateTime.MinValue;
}
