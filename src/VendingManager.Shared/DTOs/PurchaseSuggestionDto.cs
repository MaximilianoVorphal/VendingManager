namespace VendingManager.Shared.DTOs;

public class PurchaseSuggestionDto
{
    public int ProductoId { get; set; }
    public string NombreProducto { get; set; } = string.Empty;
    public int VentasPeriodo { get; set; }
    public int StockActualMaquinas { get; set; }
    public int StockBodega { get; set; }
    public int CantidadSugerida { get; set; }
    public bool EnMaquina { get; set; }
}
