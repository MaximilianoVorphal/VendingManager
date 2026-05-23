namespace VendingManager.Shared.DTOs;

public class CreateTransferenciaRequest
{
    public DateTime Fecha { get; set; } = DateTime.Now;
    public decimal Monto { get; set; }
    public string? Descripcion { get; set; }
    public string Trabajador { get; set; } = string.Empty;
}