namespace VendingManager.Shared.DTOs;

public class GastoPendienteDto
{
    public int GastoRecurrenteId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal MontoEstimado { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public int? MaquinaId { get; set; }
    public string? MaquinaNombre { get; set; }
}
