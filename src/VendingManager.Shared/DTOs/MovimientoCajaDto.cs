using System;

namespace VendingManager.Shared.DTOs
{
    public class MovimientoCajaDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string? ImagenPath { get; set; }
        public int? ProductoId { get; set; }
        public int Cantidad { get; set; }
        public int? OrdenCargaId { get; set; }
        public int? CompraId { get; set; }
        public int? GastoRecurrenteId { get; set; }
    }
}
