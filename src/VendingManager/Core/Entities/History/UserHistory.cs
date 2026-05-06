using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

/// <summary>
/// Entidad history para User. Refleja las columnas de User más los campos de auditoría.
/// </summary>
public class UserHistory
{
    [Key]
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- User base columns ---
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User";
}
