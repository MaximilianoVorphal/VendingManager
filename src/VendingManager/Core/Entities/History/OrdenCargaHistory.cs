using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

/// <summary>
/// History entity for OrdenCarga. Mirrors columns plus audit fields.
/// </summary>
public class OrdenCargaHistory
{
    [Key]
    public int Id { get; set; }
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;

    [Column(TypeName = "nvarchar(max)")]
    public string? BeforeJson { get; set; }

    [Column(TypeName = "nvarchar(max)")]
    public string? AfterJson { get; set; }

    public DateTime Timestamp { get; set; }
    public string Usuario { get; set; } = string.Empty;

    // --- OrdenCarga base columns ---
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaFinalizacion { get; set; }

    [Required]
    public string Estado { get; set; } = "PENDIENTE";

    public string? Nombre { get; set; }
    public int? MaquinaId { get; set; }
}
