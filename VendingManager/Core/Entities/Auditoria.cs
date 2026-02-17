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

    public string Detalle { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.Now;
}
