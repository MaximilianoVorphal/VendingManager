namespace VendingManager.Shared.DTOs;

public class CompraVinculadaRequest : RegistrarCompraRequestDto
{
    public int TransferenciaId { get; set; }
    public int RendicionId { get; set; }
    public string Trabajador { get; set; } = string.Empty;
}