namespace VendingManager.Shared.DTOs
{
    public class InformeFinancieroDto
    {
        public decimal VentasTotales { get; set; }
        public decimal CostoVentas { get; set; }
        public decimal MargenBruto { get; set; }
        public decimal GastosOperativos { get; set; }
        public decimal UtilidadNeta { get; set; }
        public decimal MargenPorcentaje { get; set; }

        // --- EBITDA fields (additive, backward-compatible) ---
        public decimal GastosFijos { get; set; }
        public decimal GastosVariables { get; set; }
        public decimal DepreciacionPeriodo { get; set; }
        public decimal Ebitda { get; set; }
    }
}
