namespace VendingManager.Core.DTOs;

/// <summary>
/// DTO para mostrar un template de recarga con sus períodos
/// </summary>
public class TemplateRecargaDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<PeriodoRecargaDto> Periodos { get; set; } = new();

    // Helpers para UI
    public int CantidadMaquinas => Periodos.Count;
    public DateTime? FechaInicioMin => Periodos.Any() ? Periodos.Min(p => p.FechaInicio) : null;
    public DateTime? FechaFinMax => Periodos.Any() ? Periodos.Max(p => p.FechaFin) : null;
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

    // Helper para UI
    public double DuracionHoras => (FechaFin - FechaInicio).TotalHours;
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
