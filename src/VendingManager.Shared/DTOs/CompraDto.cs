namespace VendingManager.Shared.DTOs;

public class CompraDto
{
    public int Id { get; set; }
    public DateTime FechaCompra { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
    public decimal MontoTotal { get; set; }
    public string Estado { get; set; } = "PAGADA";
    public string TipoFactura { get; set; } = "MERCADERIA";
    public bool PagadaCaja { get; set; } = true;
    public string? FacturaImagenPath { get; set; }
    public int? TransferenciaId { get; set; }
    public bool TienePendientes => Detalles?.Any(d => d.EsPendiente) ?? false;
    public int PendientesCount => Detalles?.Count(d => d.EsPendiente) ?? 0;
    public List<DetalleCompraDto> Detalles { get; set; } = new();
}

public class DetalleCompraDto
{
    public int Id { get; set; }
    public int CompraId { get; set; }
    public int? ProductoId { get; set; }
    public string? ProductoNombre { get; set; }
    public string? DescripcionItem { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal Subtotal { get; set; }
    public bool EsPendiente { get; set; }
    public string? Ean { get; set; }
    public string? Sku { get; set; }
}

public class RegistrarCompraRequestDto
{
    public DateTime FechaCompra { get; set; } = DateTime.Now;
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
    public string Estado { get; set; } = "PAGADA";
    public string TipoFactura { get; set; } = "MERCADERIA";
    public bool PagadaCaja { get; set; } = true;
    /// <summary>Subcategoría para GASTO_GENERAL: BENCINA, PEAJE, o vacío para GASTOS GENERALES.</summary>
    public string? SubcategoriaGasto { get; set; }
    public List<RegistrarDetalleCompraRequestDto> Detalles { get; set; } = new();
}

public class RegistrarDetalleCompraRequestDto
{
    public int? ProductoId { get; set; }
    public string? DescripcionItem { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public bool EsPendiente { get; set; }
    public string? Ean { get; set; }
    public string? Sku { get; set; }
    /// <summary>Cantidad de unidades por pack (null = unitario). Se usa para el aprendizaje EAN.</summary>
    public int? PackSize { get; set; }
}

public class ActualizarCompraRequestDto
{
    public DateTime FechaCompra { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? NumeroDocumento { get; set; }
}
