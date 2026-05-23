using VendingManager.Shared.Enums;

namespace VendingManager.Shared.DTOs;

public class RendicionDto
{
    public int Id { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public RendicionEstado Estado { get; set; }
    public string? Observaciones { get; set; }
    public List<TransferenciaDto> Transferencias { get; set; } = new();
    public List<MovimientoCajaDto> Gastos { get; set; } = new();
}