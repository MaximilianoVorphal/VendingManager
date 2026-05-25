namespace VendingManager.Shared.DTOs;

/// <summary>
/// Resultado de la reconstrucción de costos desde compras históricas.
/// </summary>
public class ReconstruirCostosResult
{
    public int ProductosProcesados { get; set; }
    public int ComprasReprocesadas { get; set; }
    public int RegistrosProductoCostoCreados { get; set; }
    public int DetallesSinProducto { get; set; }
}