namespace VendingManager.Shared.DTOs;

public class HistorialCostoViewDto
{
    public DateTime Fecha { get; set; }
    public decimal CostoUnitario { get; set; }
    public string Origen { get; set; } = string.Empty;
}
