namespace VendingManager.Shared.DTOs;

public class CategoriaAnalisisDto
{
    public string Nombre { get; set; } = string.Empty;
    public decimal TotalVentas { get; set; }
    public decimal TotalGanancia { get; set; }
    public int CantidadVendida { get; set; }
    public decimal PorcentajeDelTotal { get; set; }
}