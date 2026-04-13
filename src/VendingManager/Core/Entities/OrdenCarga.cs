using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities
{
    public class OrdenCarga
    {
        [Key]
        public int Id { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;
        public DateTime? FechaFinalizacion { get; set; }

        [Required]
        public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE, FINALIZADA

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
        public string ProductoNombre { get; set; } = ""; // Snapshot name

        public int CantidadSolicitada { get; set; } // Qty taken from warehouse
        public int CantidadRetornada { get; set; } = 0; // Qty returned (sobras)

        public int? MaquinaId { get; set; } // For consolidated orders
    }
}
