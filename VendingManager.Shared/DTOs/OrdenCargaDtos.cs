using System;
using System.Collections.Generic;

namespace VendingManager.Shared.DTOs
{
    public class CrearOrdenDto
    {
        public int MaquinaId { get; set; }
        public List<DetalleOrdenCargaItemDto> Items { get; set; } = new();
    }

    public class DetalleOrdenCargaItemDto
    {
        public int ProductoId { get; set; }
        public int Cantidad { get; set; }
    }

    public class FinalizarOrdenDto
    {
        public int OrdenId { get; set; }
        public List<DetalleOrdenRetornoDto> Retornos { get; set; } = new();
    }

    public class DetalleOrdenRetornoDto
    {
        public int DetalleId { get; set; } // Or ProductId, but DetalleId is safer to map back
        public int CantidadRetornada { get; set; }
    }

    public class OrdenCargaDto
    {
        public int Id { get; set; }
        public DateTime FechaCreacion { get; set; }
        public string Estado { get; set; } = "";
        public int MaquinaId { get; set; }
        public string MaquinaNombre { get; set; } = "";
        public List<DetalleOrdenDisplayDto> Detalles { get; set; } = new();
    }

    public class DetalleOrdenDisplayDto
    {
        public int Id { get; set; } // DetalleId
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = "";
        public int CantidadSolicitada { get; set; }
        public int CantidadRetornada { get; set; }
    }
}
