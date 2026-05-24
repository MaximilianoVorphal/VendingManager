namespace VendingManager.Shared.DTOs;

public class UpdateGastoRequest
{
    public string? Descripcion { get; set; }
    public decimal? Monto { get; set; }
    public string? Categoria { get; set; }
}
