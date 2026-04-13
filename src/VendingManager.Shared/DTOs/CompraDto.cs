namespace VendingManager.Shared.DTOs;

public class CompraDto
{
    public int Id { get; set; }
    public DateTime FechaCompra { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
    public decimal MontoTotal { get; set; }
    public string Estado { get; set; } = "PAGADA";
    public bool PagadaCaja { get; set; } = true;
    public List<DetalleCompraDto> Detalles { get; set; } = new();
}

public class DetalleCompraDto
{
    public int Id { get; set; }
    public int CompraId { get; set; }
    public int ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal Subtotal { get; set; }
}

public class RegistrarCompraRequestDto
{
    public DateTime FechaCompra { get; set; } = DateTime.Now;
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
    public string Estado { get; set; } = "PAGADA";
    public bool PagadaCaja { get; set; } = true;
    public List<RegistrarDetalleCompraRequestDto> Detalles { get; set; } = new();
}

public class RegistrarDetalleCompraRequestDto
{
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
}

public class ActualizarCompraRequestDto
{
    public DateTime FechaCompra { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
}
