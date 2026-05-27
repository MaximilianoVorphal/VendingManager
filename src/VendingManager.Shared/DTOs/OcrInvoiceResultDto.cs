using System.Text.Json.Serialization;

namespace VendingManager.Shared.DTOs
{
    public class OcrInvoiceResultDto
    {
        [JsonPropertyName("proveedor")]
        public string? Proveedor { get; set; }
        
        [JsonPropertyName("numero_documento")]
        public string? NumeroDocumento { get; set; }
        
        [JsonPropertyName("fecha")]
        public string? Fecha { get; set; }
        
        [JsonPropertyName("monto_total")]
        public decimal MontoTotal { get; set; }
        
        [JsonPropertyName("tipo_documento")]
        public string? TipoDocumento { get; set; }

        [JsonPropertyName("total_neto")]
        public decimal? TotalNeto { get; set; }

        [JsonPropertyName("total_iva")]
        public decimal? TotalIva { get; set; }

        [JsonPropertyName("total_ila")]
        public decimal? TotalIla { get; set; }

        [JsonPropertyName("items")]
        public List<OcrInvoiceItemDto> Items { get; set; } = new();
    }

    public class OcrInvoiceItemDto
    {
        [JsonPropertyName("producto")]
        public string? Producto { get; set; }
        
        [JsonPropertyName("cantidad")]
        public decimal Cantidad { get; set; }
        
        [JsonPropertyName("costo_unitario")]
        public decimal CostoUnitario { get; set; }
        
        [JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }
        
        public int? ProductoIdMatch { get; set; }
        
        [JsonPropertyName("sugerir_creacion")]
        public bool SugerirCreacion { get; set; }

        [JsonPropertyName("ean")]
        public string? Ean { get; set; }

        [JsonPropertyName("sku")]
        public string? Sku { get; set; }

        [JsonPropertyName("tiene_iva")]
        public bool TieneIva { get; set; }

        [JsonPropertyName("tiene_ila")]
        public bool TieneIla { get; set; }

        [JsonPropertyName("tipo_ila")]
        public string? TipoIla { get; set; }

        [JsonPropertyName("neto_unitario")]
        public decimal? NetoUnitario { get; set; }

        /// <summary>Indica que el item es un pack y se desglosó en múltiples unidades. El UI debe mostrar confirmación.</summary>
        [JsonPropertyName("requiere_confirmacion_pack")]
        public bool RequiereConfirmacionPack { get; set; }
    }
}
