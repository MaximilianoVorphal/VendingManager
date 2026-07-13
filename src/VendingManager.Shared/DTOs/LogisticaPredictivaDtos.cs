using System;
using System.Collections.Generic;

namespace VendingManager.Shared.DTOs
{
    /// <summary>
    /// Análisis logístico agrupado por zona: LCP (lucro cesante proyectado) total
    /// de la zona vs el costo base de enviar un vehículo.
    /// </summary>
    public class LogisticaZonaDto
    {
        public int? ZonaLogisticaId { get; set; } // Null = máquinas sin zona asignada
        public string ZonaNombre { get; set; } = "Sin zona";
        public decimal CostoBaseViaje { get; set; }
        public decimal LcpTotal { get; set; }
        public bool EsRentableViajar { get; set; } // LcpTotal > CostoBaseViaje (false sin zona/costo)
        public List<LogisticaMaquinaDto> Maquinas { get; set; } = new();
    }

    public class LogisticaMaquinaDto
    {
        public int MaquinaId { get; set; }
        public string MaquinaNombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public decimal LcpMaquina { get; set; }
        public List<LogisticaSlotDto> Slots { get; set; } = new();
    }

    public class LogisticaSlotDto
    {
        public int SlotId { get; set; }
        public string NumeroSlot { get; set; } = string.Empty;
        public int ProductoId { get; set; }
        public string ProductoNombre { get; set; } = string.Empty;
        public int StockActual { get; set; }
        public int CapacidadMaxima { get; set; }
        public int UnidadesFaltantes { get; set; } // CapacidadMaxima - StockActual
        public decimal VelocidadDiaria { get; set; } // Unidades/día en la ventana de historial
        public double? DiasHastaQuiebre { get; set; } // Null cuando no hay velocidad
        public bool EsCritico { get; set; } // Quiebre proyectado en menos de 48h
        public decimal MargenUnitario { get; set; } // PrecioVenta slot - CostoPromedio producto (>= 0)
        public decimal LcpSlot { get; set; } // Margen x velocidad x días vacíos proyectados
    }
}
