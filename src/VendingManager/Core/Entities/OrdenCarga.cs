using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using VendingManager.Shared.Enums;

namespace VendingManager.Core.Entities
{
    public class OrdenCarga
    {
        [Key]
        public int Id { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public DateTime? FechaFinalizacion { get; set; }

        [Required]
        public EstadoOrdenCarga Estado { get; set; } = EstadoOrdenCarga.Pendiente;

        public string? Nombre { get; set; } // Opcional, nombre de la ruta/orden

        public int? MaquinaId { get; set; } // Null if Global Order
        // Navigation property not strictly necessary if we just use ID, but good for EF
        // public Maquina Maquina { get; set; } 

        public List<DetalleOrdenCarga> Detalles { get; set; } = new();
    }

    public class DetalleOrdenCarga
    {
        [Key]
        public int Id { get; set; }

        public int OrdenCargaId { get; set; }
        // public OrdenCarga OrdenCarga { get; set; }

        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = ""; // Nombre snapshot al momento de crear la orden

        public int CantidadSolicitada { get; set; } // Cantidad tomada del stock de bodega
        public int CantidadRetornada { get; set; } = 0; // Cantidad retornada (sobras devueltas)
        public decimal CostoUnitario { get; set; } // Snapshot de CostoPromedio al momento de crear la orden

        public int? MaquinaId { get; set; } // Para órdenes consolidadas
    }
}
