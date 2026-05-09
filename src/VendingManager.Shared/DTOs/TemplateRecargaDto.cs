namespace VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

/// <summary>
    /// DTO para mostrar un template de recarga con sus períodos
    /// </summary>
public class TemplateRecargaDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public DateTime FechaCreacion { get; set; }

    /// <summary>
    /// Indica si este template tiene una foto guía persisted
    /// </summary>
    public bool TieneFotoGuia { get; set; }

    /// <summary>
    /// Indica si este template tiene una foto OCR persisted
    /// </summary>
    public bool TieneFotoOcr { get; set; }

    public List<PeriodoRecargaDto> Periodos { get; set; } = new();

    // Helpers para UI
    public int CantidadMaquinas => Periodos.Count;
    public DateTime? FechaInicioMin => Periodos.Any() ? Periodos.Min(p => p.FechaInicio) : null;
    public DateTime? FechaFinMax => Periodos.Any() ? Periodos.Max(p => p.FechaFin) : null;
    public int CantidadSlotsPendientes => Periodos
        .SelectMany(p => p.SnapshotSlots)
        .Count(s => s.Estado == EstadoSlot.Pendiente);
}

/// <summary>
/// DTO para un período específico de máquina
/// </summary>
public class PeriodoRecargaDto
{
    public int Id { get; set; }
    public int MaquinaId { get; set; }
    public string MaquinaNombre { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }

    /// <summary>
    /// Snapshot del inventario de slots al momento de la recarga
    /// </summary>
    public List<SnapshotSlotDto> SnapshotSlots { get; set; } = new();

    // Helper para UI
    public double DuracionHoras => (FechaFin - FechaInicio).TotalHours;
    public bool TieneSnapshot => SnapshotSlots.Any();
}

/// <summary>
/// DTO para crear un nuevo template de recarga
/// </summary>
public class CreateTemplateRecargaDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public List<CreatePeriodoDto> Periodos { get; set; } = new();
}

/// <summary>
/// DTO para crear un período dentro de un template
/// </summary>
public class CreatePeriodoDto
{
    public int MaquinaId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }

    /// <summary>
    /// Snapshot del inventario de slots (opcional)
    /// </summary>
    public List<CreateSnapshotSlotDto> SnapshotSlots { get; set; } = new();
}

/// <summary>
/// DTO para actualizar un template existente
/// </summary>
public class UpdateTemplateRecargaDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public List<CreatePeriodoDto> Periodos { get; set; } = new();
}

/// <summary>
/// DTO para mostrar un snapshot de slot
/// </summary>
public class SnapshotSlotDto
{
    public int Id { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;
    public int? ProductoId { get; set; }
    public string ProductoNombre { get; set; } = string.Empty;
    public int CantidadInicial { get; set; }
    public int CapacidadSlot { get; set; }
    public EstadoSlot Estado { get; set; } = EstadoSlot.Vacio;
}

/// <summary>
/// DTO para crear un snapshot de slot
/// </summary>
public class CreateSnapshotSlotDto
{
    public string NumeroSlot { get; set; } = string.Empty;
    public int? ProductoId { get; set; }
    public int CantidadInicial { get; set; }
    public int CapacidadSlot { get; set; }
    public EstadoSlot Estado { get; set; } = EstadoSlot.Vacio;
}

/// <summary>
/// Resultado de sincronizar un slot específico con ventas históricas
/// </summary>
public class SyncSlotProductoResultDto
{
    public int MaquinaId { get; set; }
    public string NumeroSlot { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public int VentasActualizadas { get; set; }
}

/// <summary>
/// Request para sincronizar el producto de un slot
/// </summary>
public class SyncSlotProductoRequestDto
{
    public int ProductoId { get; set; }
}

