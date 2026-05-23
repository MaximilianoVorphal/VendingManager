namespace VendingManager.Shared.DTOs;

public class RendicionFullDto : RendicionDto
{
    public RendicionResumenDto Resumen { get; set; } = new();
}