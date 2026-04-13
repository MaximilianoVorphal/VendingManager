namespace VendingManager.Shared.DTOs;

public class AuditoriaDto
{
    public int Id { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Accion { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
}
