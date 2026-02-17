
namespace VendingManager.Shared.DTOs
{
    public class StockCriticoDto
    {
        public int SlotId { get; set; }
        public string Maquina { get; set; } = string.Empty;
        public string NumeroSlot { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public int ProductoId { get; set; }
        public int StockActual { get; set; }
        public int CapacidadMaxima { get; set; }
    }
}
