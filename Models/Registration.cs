using System.ComponentModel.DataAnnotations;

namespace MyDiscordBot.Models;

public class Registration
{
    [Key]
    public int Id { get; set; }

    // Metadata
    public ulong DiscordUserId { get; set; }
    public string DiscordUsername { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; } = DateTime.Now;

    // Matchmaking Data (NUEVO)
    public int? GroupId { get; set; } // ID numérico del grupo (1, 2, 3...)
    public string? TeamRoleName { get; set; } // Nombre del rol asignado (ej: "Grupo #1")

    // Paso 1: Identidad
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string DNI { get; set; } = string.Empty;
    public string Edad { get; set; } = string.Empty;
    public string Ubicacion { get; set; } = string.Empty;

    // Paso 2: Perfil
    public string Experiencia { get; set; } = string.Empty;
    public string Roles { get; set; } = string.Empty;
    public string TipoParticipacion { get; set; } = string.Empty;

    // Paso 3: Logística
    public string Disponibilidad { get; set; } = string.Empty;
    public bool ConsentimientoDifusion { get; set; }

    // Paso 4: Cierre
    public string NecesidadesEspeciales { get; set; } = string.Empty;
}
