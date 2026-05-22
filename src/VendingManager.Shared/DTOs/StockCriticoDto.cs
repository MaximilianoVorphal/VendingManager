
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

        /// <summary>
        /// Fuente de los datos: "template" cuando viene del template Activo,
        /// "configuracion" cuando viene de ConfiguracionSlots (fallback).
        /// </summary>
        public string Fuente { get; set; } = "configuracion";
    }
}
