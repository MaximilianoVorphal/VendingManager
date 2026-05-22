namespace VendingManager.Shared.Enums;

/// <summary>
/// Estado del ciclo de vida de un TemplateRecarga.
/// </summary>
public enum EstadoTemplate
{
    /// <summary>Template en desarrollo, sin carga activa</summary>
    Borrador = 0,

    /// <summary>Carga en curso — slots siendo populados en campo</summary>
    EnCarga = 1,

    /// <summary>Template completado y activo — slots son fuente autoritativa</summary>
    Activo = 2,

    /// <summary>Template cerrado — histórico, no permite transiciones</summary>
    Cerrado = 3
}