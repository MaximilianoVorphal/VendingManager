using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Web.Pages.Contabilidad.State;

public class ContabilidadPageState
{
    public List<RendicionDto> Rendiciones { get; set; } = new();
    public RendicionDto? RendicionActiva { get; set; }
    public RendicionFullDto? RendicionActivaFull { get; set; }

    public decimal TotalTransferido => RendicionActivaFull?.Resumen.Transferido ?? 0;
    public decimal Devuelto => RendicionActivaFull?.Resumen.Devuelto ?? 0;

    // MERCADERIA only — GASTO_GENERAL compras are reclassified into TotalGastos
    public decimal TotalCompras => Compras
        .Where(c => c.TipoFactura == "MERCADERIA")
        .Sum(c => c.MontoTotal);

    // GASTO_GENERAL compras + MovimientoCaja gastos (excludes RETIRO_CAPITAL / DEVOLUCION)
    public decimal TotalGastos =>
        Compras.Where(c => c.TipoFactura != "MERCADERIA").Sum(c => c.MontoTotal)
        + (RendicionActivaFull?.Resumen.TotalGastos ?? 0);

    public decimal Diferencia => TotalTransferido - TotalCompras - TotalGastos;
    public decimal SaldoADevolver => Diferencia - Devuelto;

    public bool CanCuadrar
    {
        get
        {
            if (RendicionActivaFull == null) return false;
            if (RendicionActivaFull.Transferencias.Count == 0) return false;
            if (SaldoADevolver != 0) return false;
            if (RendicionActivaFull.Transferencias.Any(t => !t.Verificada)) return false;
            if (RendicionActivaFull.Transferencias.SelectMany(t => t.Compras).Any(c => !c.Verificada)) return false;
            return true;
        }
    }

    public List<TransferenciaDto> Transferencias => RendicionActivaFull?.Transferencias ?? new();
    public List<MovimientoCajaDto> Gastos => RendicionActivaFull?.Gastos ?? new();

    public List<CompraDto> Compras
    {
        get
        {
            if (RendicionActivaFull == null) return new();
            return RendicionActivaFull.Transferencias
                .SelectMany(t => t.Compras)
                .ToList();
        }
    }

    public List<CompraDto> ComprasMercaderia =>
        Compras.Where(c => c.TipoFactura == "MERCADERIA").ToList();

    public List<CompraDto> ComprasGastoGeneral =>
        Compras.Where(c => c.TipoFactura != "MERCADERIA").ToList();

    public bool EstaAbierta => RendicionActiva?.Estado == RendicionEstado.Abierta;

    public void Limpiar()
    {
        Rendiciones.Clear();
        RendicionActiva = null;
        RendicionActivaFull = null;
    }

    public void Dispose() { }
}
