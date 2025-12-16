
namespace VendingManager.Core.DTOs
{
    public class ReporteDto
    {
        public int TotalVentas { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public decimal GananciaTotal { get; set; }
        public List<DetalleVentaDto> Detalle { get; set; } = new();
    }

    public class DetalleVentaDto
    {

        public DateTime FechaRaw { get; set; }
        public string Maquina { get; set; } = "";
        public int Slot { get; set; }
        public string Producto { get; set; } = "";
        public decimal Monto { get; set; }
        public decimal CostoUnitario { get; set; }
        public decimal Ganancia { get; set; }
        public string Estado { get; set; } = "";
    }
}
