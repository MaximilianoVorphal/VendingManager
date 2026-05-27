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
    }
}
