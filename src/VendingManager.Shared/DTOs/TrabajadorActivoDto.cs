namespace VendingManager.Shared.DTOs;

public class TrabajadorActivoDto
{
    public string Nombre { get; set; } = string.Empty;
    public int? RendicionActivaId { get; set; }
    public int RendicionesAbiertas { get; set; }
}