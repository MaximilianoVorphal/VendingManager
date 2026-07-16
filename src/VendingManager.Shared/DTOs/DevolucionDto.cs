using System.ComponentModel.DataAnnotations;

namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO for reading a Devolucion returned from the API.
/// </summary>
public class DevolucionDto
{
    public int Id { get; set; }
    public decimal Monto { get; set; }
    public DateTime Fecha { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
    public int? PeriodoId { get; set; }
    public int? RendicionId { get; set; }
}

/// <summary>
/// Request DTO for registering a new Devolucion via the API.
/// </summary>
public class RegistrarDevolucionRequest
{
    /// <summary>Target accounting period (primary linkage for the /contabilidad UI).</summary>
    public int? PeriodoId { get; set; }

    /// <summary>Target rendicion (secondary linkage for the rendicion API path).</summary>
    public int? RendicionId { get; set; }

    /// <summary>Worker returning the money.</summary>
    [Required]
    public string Trabajador { get; set; } = string.Empty;

    /// <summary>Amount being returned. Must be positive and ≤ SaldoADevolver.</summary>
    public decimal Monto { get; set; }

    /// <summary>Date of the return.</summary>
    public DateTime Fecha { get; set; } = DateTime.Today;

    /// <summary>Optional notes.</summary>
    public string? Observaciones { get; set; }
}
