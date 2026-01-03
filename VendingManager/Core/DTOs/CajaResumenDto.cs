namespace VendingManager.Core.DTOs
{
    public class CajaResumenDto
    {
        public decimal SaldoAnterior { get; set; }
        public decimal IngresosVentas { get; set; }
        public decimal GastosOperativos { get; set; }
        public decimal AportesExtra { get; set; }
        public decimal SaldoFinal { get; set; }
        public decimal UtilidadTotal { get; set; }
        public decimal GastosMercaderia { get; set; }
        public decimal TotalCostoVenta { get; set; } 
        public decimal Mermas { get; set; }          // [NEW]
        public decimal GastosVariables { get; set; } // [NEW] Logistica
        public decimal GastosFijos { get; set; }     // [NEW] Estructurales
        public decimal UtilidadOperacional { get; set; } // [NEW] EBITDA
        public decimal SueldoEsperado { get; set; }  // [NEW] Target Salary
        public decimal UtilidadNeta { get; set; }    // Renamed semantic intent to Real Net
        public bool IsLocked { get; set; }
    }
}
