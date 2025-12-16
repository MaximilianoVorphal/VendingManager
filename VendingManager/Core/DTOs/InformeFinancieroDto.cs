namespace VendingManager.Core.DTOs
{
    public class InformeFinancieroDto
    {
        public decimal VentasTotales { get; set; }
        public decimal CostoVentas { get; set; }
        public decimal MargenBruto { get; set; }
        public decimal GastosOperativos { get; set; }
        public decimal UtilidadNeta { get; set; }
        public decimal MargenPorcentaje { get; set; }
    }
}
