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

/// <summary>
/// Request para crear un cuadre completo: un período contable con una única
/// transferencia (relación 1:1). Cada cuadre es una hoja independiente que se
/// concilia contra sus propias compras y gastos.
/// </summary>
public class CrearCuadreRequest
{
    public DateTime Fecha { get; set; } = DateTime.Now;
    public decimal Monto { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}

/// <summary>
/// Identificadores del cuadre recién creado (período + su transferencia 1:1).
/// </summary>
public class CuadreCreadoDto
{
    public int PeriodoId { get; set; }
    public int TransferenciaId { get; set; }
}