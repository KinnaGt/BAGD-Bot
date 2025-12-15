using Microsoft.EntityFrameworkCore;
using MyDiscordBot.Models;

namespace MyDiscordBot.Data;

public class AppDbContext : DbContext
{
    public DbSet<Registration> Registrations { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=bagd_jam.db");
        }
    }
}
