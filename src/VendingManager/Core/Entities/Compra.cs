using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VendingManager.Core.Entities;

public class Compra
{
    [Key]
    public int Id { get; set; }

    public DateTime FechaCompra { get; set; } = DateTime.Now;

    [Required]
    public string Proveedor { get; set; } = string.Empty;

    public string? NumeroDocumento { get; set; } = string.Empty; // Factura o Boleta

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoTotal { get; set; } = 0;

    public string Estado { get; set; } = "PAGADA"; // PAGADA, PENDIENTE
    
    public string TipoFactura { get; set; } = "MERCADERIA"; // MERCADERIA, GASTO_GENERAL
    
    // Si queremos trazar si la plata ya salió de caja (útil para "Cuentas por Pagar" a futuro)
    public bool PagadaCaja { get; set; } = true;

    // Usuario que registró la compra (opcional, para auditoría)
    public string? UsuarioRegistra { get; set; }

    // Legacy: relative path to the factura/boleta image on disk
    // (e.g. /uploads/compras/facturas/{guid}.jpg). Kept for backward-compatible
    // reads and as the source for the DB backfill; new uploads store the image
    // bytes in FacturaImagen instead. Also acts as the "has image" flag exposed
    // to the frontend.
    public string? FacturaImagenPath { get; set; }

    // Image bytes stored directly in the database. Preferred over the on-disk
    // path so the image travels with the DB (backups, dev environments).
    public byte[]? FacturaImagen { get; set; }

    // MIME type of FacturaImagen (e.g. "image/jpeg", "application/pdf"), needed
    // to serve the bytes since there is no file extension to infer from.
    [MaxLength(100)]
    public string? FacturaImagenContentType { get; set; }

    /// <summary>
    /// Indicates whether the compra comprobante has been verified by the owner.
    /// Defaults to false; historic rows are explicitly unverified after migration.
    /// </summary>
    public bool Verificada { get; set; } = false;

    [MaxLength(200)]
    public string? Trabajador { get; set; }

    // Vinculación con Transferencia (nullable FK — sin ruptura de cambios existentes)
    public int? TransferenciaId { get; set; }
    public Transferencia? Transferencia { get; set; }

    // Vinculación con ProveedorCatalog (nullable FK — null == PENDING, sin ruptura de cambios existentes)
    public int? ProveedorCatalogId { get; set; }
    public ProveedorCatalog? ProveedorCatalog { get; set; }

    /// <summary>Subcategoría para gastos: BENCINA, PEAJE, GASTOS GENERALES. No persiste en DB, solo se usa en el flujo de creación.</summary>
    [NotMapped]
    public string? SubcategoriaGasto { get; set; }

    public List<DetalleCompra> Detalles { get; set; } = new();
}
