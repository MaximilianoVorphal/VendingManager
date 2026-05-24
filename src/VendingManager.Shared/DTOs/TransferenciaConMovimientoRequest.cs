namespace VendingManager.Shared.DTOs;

public class TransferenciaConMovimientoRequest
{
    public int RendicionId { get; set; }
    public int? PeriodoId { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Now;
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
}