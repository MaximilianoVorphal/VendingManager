using VendingManager.Shared.Enums;

namespace VendingManager.Shared.DTOs;

public class TransferenciaDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public TransferenciaEstado Estado { get; set; }
    public int? RendicionId { get; set; }
    public int? PeriodoId { get; set; }
    public int? MovimientoCajaId { get; set; }
    public bool Verificada { get; set; } = false;
    public bool HasComprobante { get; set; }
    public string? ComprobanteImagenFileName { get; set; }
    public List<CompraDto> Compras { get; set; } = new();
}