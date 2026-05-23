namespace VendingManager.Shared.DTOs;

public class GastoVinculadoRequest
{
    public int RendicionId { get; set; }
    public string Trabajador { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Now;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string Categoria { get; set; } = "GENERAL";
}