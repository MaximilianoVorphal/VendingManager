namespace VendingManager.Shared.DTOs;

/// <summary>
/// DTO for a ZonaLogistica entry, used in API responses.
/// </summary>
public class ZonaLogisticaDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal CostoBaseViaje { get; set; }
}

/// <summary>
/// Request DTO for creating a new ZonaLogistica.
/// </summary>
public class CrearZonaRequestDto
{
    public string Nombre { get; set; } = string.Empty;
    public decimal CostoBaseViaje { get; set; }
}

/// <summary>
/// Request DTO for updating an existing ZonaLogistica.
/// </summary>
public class ActualizarZonaRequestDto
{
    public string Nombre { get; set; } = string.Empty;
    public decimal CostoBaseViaje { get; set; }
}
