
namespace VendingManager.Shared.DTOs
{
    public class ReporteDto
    {
        public int TotalVentas { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public decimal MontoPhantom { get; set; } // Cobros extra (TB-EXTRA)
        public decimal GananciaTotal { get; set; }
        public List<DetalleVentaDto> Detalle { get; set; } = new();
        public List<DetalleVentaDto> Fantasmas { get; set; } = new(); // Nueva lista separada para el modal
    }

    public class DetalleVentaDto
    {

        public DateTime FechaRaw { get; set; }
        public string Maquina { get; set; } = "";
        /// <summary>Código interno de la máquina (ej: "2410280022").</summary>
        public string IdInternoMaquina { get; set; } = "";
        public string Slot { get; set; } = "";
        public string Producto { get; set; } = "";
        public decimal Monto { get; set; }
        public decimal CostoUnitario { get; set; }
        public decimal Ganancia { get; set; }
        public string Estado { get; set; } = "";
    }
}
