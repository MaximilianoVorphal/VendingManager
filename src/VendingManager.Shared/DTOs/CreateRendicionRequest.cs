namespace VendingManager.Shared.DTOs;

public class CreateRendicionRequest
{
    public string Trabajador { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; } = DateTime.Now;
    public string? Observaciones { get; set; }
    /// <summary>Optional initial transferencia id to link on creation.</summary>
    public int? TransferenciaId { get; set; }
}