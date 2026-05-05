using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities;

public class Auditoria
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Usuario { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Accion { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    [MaxLength(200)]
    public string? EntityType { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string Detalle { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.Now;
}
