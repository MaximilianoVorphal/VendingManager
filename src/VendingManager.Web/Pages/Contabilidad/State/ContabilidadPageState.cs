using VendingManager.Shared.DTOs;

namespace VendingManager.Web.Pages.Contabilidad.State;

public class ContabilidadPageState
{
    public RendicionFullDto? ActiveRendicion { get; set; }
    public List<TransferenciaDto> Transferencias { get; set; } = new();
    public List<CompraDto> Compras { get; set; } = new();
    public List<MovimientoCajaDto> Gastos { get; set; } = new();
    public int CurrentStep { get; set; } = 1;
    public string? SelectedWorker { get; set; }
    public RendicionResumenDto? LiveReconciliation { get; set; }

    public decimal TotalTransferido => Transferencias.Sum(t => t.Monto);
    public decimal TotalCompras => Compras.Sum(c => c.MontoTotal);
    public decimal TotalGastos => Gastos.Sum(g => Math.Abs(g.Monto));
    public decimal Diferencia => TotalTransferido - TotalCompras - TotalGastos;

    public void Recalcular()
    {
        if (LiveReconciliation != null)
        {
            LiveReconciliation.Transferido = TotalTransferido;
            LiveReconciliation.TotalCompras = TotalCompras;
            LiveReconciliation.TotalGastos = TotalGastos;
            LiveReconciliation.Diferencia = Diferencia;
        }
        else
        {
            LiveReconciliation = new RendicionResumenDto
            {
                Transferido = TotalTransferido,
                TotalCompras = TotalCompras,
                TotalGastos = TotalGastos,
                Diferencia = Diferencia
            };
        }
    }

    public void AvanzarPaso() { if (CurrentStep < 5) CurrentStep++; }
    public void RetrocederPaso() { if (CurrentStep > 1) CurrentStep--; }
    public void IrAlPaso(int paso) { if (paso >= 1 && paso <= 5) CurrentStep = paso; }
    public void Limpiar()
    {
        ActiveRendicion = null;
        Transferencias.Clear();
        Compras.Clear();
        Gastos.Clear();
        CurrentStep = 1;
        SelectedWorker = null;
        LiveReconciliation = null;
    }

    public void Dispose() { }
}