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

    // Paso 1: Identidad (Modal)
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string DNI { get; set; } = string.Empty;
    public string Edad { get; set; } = string.Empty; // Antes FechaNacimiento
    public string Ubicacion { get; set; } = string.Empty;

    // Paso 2: Perfil (Select Menus)
    public string Experiencia { get; set; } = string.Empty; // Principiante, Intermedio, Avanzado
    public string Roles { get; set; } = string.Empty; // Programador, Artista, etc.
    public string TipoParticipacion { get; set; } = string.Empty; // Solo vs Grupo

    // Paso 3: Logística (Select Menus)
    public string Disponibilidad { get; set; } = string.Empty; // Franjas horarias combinadas
    public bool ConsentimientoDifusion { get; set; } // Checkbox logic

    // Paso 4: Cierre (Modal)
    public string NecesidadesEspeciales { get; set; } = string.Empty;
}
