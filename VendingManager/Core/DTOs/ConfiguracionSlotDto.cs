
namespace VendingManager.Core.DTOs
{
    public class ConfiguracionSlotDto
    {
        public int Id { get; set; }
        public int MaquinaId { get; set; }
        public string NumeroSlot { get; set; } = string.Empty;
        public int? ProductoId { get; set; }
        public ProductoSlotDto? Producto { get; set; }
        public int StockActual { get; set; }
        public int CapacidadMaxima { get; set; }
        public decimal PrecioVenta { get; set; }
    }

    public class ProductoSlotDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string CodigoBarras { get; set; } = string.Empty;
        public int StockBodega { get; set; }
    }
}
