using System;
using System.Collections.Generic;

namespace VendingManager.Shared.DTOs
{
    public class CrearOrdenDto
    {
        public string? Nombre { get; set; } // Opcional
        public int? MaquinaId { get; set; } // Null for Global/Consolidated
        public List<DetalleOrdenCargaItemDto> Items { get; set; } = new();
        public DateTime? Fecha { get; set; }
        public bool IgnorarStock { get; set; }
    }

    public class DetalleOrdenCargaItemDto
    {
        public int ProductoId { get; set; }
        public int Cantidad { get; set; }
        public int? MaquinaId { get; set; } // Required if OrdenCarga.MaquinaId is null
    }

    public class FinalizarOrdenDto
    {
        public int OrdenId { get; set; }
        public List<DetalleOrdenRetornoDto> Retornos { get; set; } = new();
    }

    public class DetalleOrdenRetornoDto
    {
        public int DetalleId { get; set; } 
        public int CantidadRetornada { get; set; }
    }

    public class OrdenCargaDto
    {
        public int Id { get; set; }
        public string? Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Estado { get; set; } = "";
        public int? MaquinaId { get; set; }
        public string MaquinaNombre { get; set; } = "";
        /// <summary>Código interno de la máquina (ej: "2410280022").</summary>
        public string IdInternoMaquina { get; set; } = "";
        public decimal CostoTotal { get; set; } // Valorized cost of loaded merchandise
        public List<DetalleOrdenDisplayDto> Detalles { get; set; } = new();
    }

    public class DetalleOrdenDisplayDto
    {
        public int Id { get; set; } 
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = "";
        public int CantidadSolicitada { get; set; }
        public int CantidadRetornada { get; set; }
        public decimal CostoUnitario { get; set; }
        public int? MaquinaId { get; set; }
        public string MaquinaNombre { get; set; } = "";
        /// <summary>Código interno de la máquina (ej: "2410280022").</summary>
        public string IdInternoMaquina { get; set; } = "";
    }

    public class ActualizarOrdenRequestDto
    {
        public string? Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}
