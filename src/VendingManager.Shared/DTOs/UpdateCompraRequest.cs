namespace VendingManager.Shared.DTOs;

public class UpdateCompraRequest
{
    public string? Proveedor { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Tipo { get; set; }
    public List<UpdateDetalleCompraRequest>? Detalles { get; set; }
}

public class UpdateDetalleCompraRequest
{
    /// <summary>
    /// 0 for new items, >0 for existing items that will be replaced.
    /// </summary>
    public int Id { get; set; }
    public int? ProductoId { get; set; }
    public string? DescripcionItem { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
}
