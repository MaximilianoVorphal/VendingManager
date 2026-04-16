using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;

namespace VendingManager.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class GastoRecurrenteController : ControllerBase
{
    private readonly IGastoRecurrenteService _service;
    private readonly IAuditService _auditService;

    public GastoRecurrenteController(IGastoRecurrenteService service, IAuditService auditService)
    {
        _service = service;
        _auditService = auditService;
    }

    /// <summary>
    /// Obtiene todos los gastos recurrentes (activos e inactivos).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<GastoRecurrente>>> GetTodos()
    {
        return await _service.GetTodosAsync();
    }

    /// <summary>
    /// Crea un nuevo gasto recurrente.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GastoRecurrente>> Crear([FromBody] GastoRecurrente gasto)
    {
        var resultado = await _service.CrearAsync(gasto);
        await _auditService.RegistrarAccionAsync(
            User.Identity?.Name ?? "Desconocido",
            "Crear Gasto Recurrente",
            $"Gasto: {gasto.Descripcion}, Monto: ${gasto.MontoEstimado:N0}, Categoría: {gasto.Categoria}");
        return Ok(resultado);
    }

    /// <summary>
    /// Actualiza un gasto recurrente existente.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] GastoRecurrente gasto)
    {
        try
        {
            await _service.ActualizarAsync(id, gasto);
            await _auditService.RegistrarAccionAsync(
                User.Identity?.Name ?? "Desconocido",
                "Actualizar Gasto Recurrente",
                $"ID: {id}, Nuevo Monto: ${gasto.MontoEstimado:N0}");
            return Ok("Gasto recurrente actualizado.");
        }
        catch (Exception ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// Desactiva un gasto recurrente (soft-delete).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Desactivar(int id)
    {
        try
        {
            await _service.DesactivarAsync(id);
            await _auditService.RegistrarAccionAsync(
                User.Identity?.Name ?? "Desconocido",
                "Desactivar Gasto Recurrente",
                $"ID: {id}");
            return Ok("Gasto recurrente desactivado.");
        }
        catch (Exception ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// Obtiene los gastos recurrentes pendientes de registrar en un mes/año.
    /// </summary>
    [HttpGet("pendientes")]
    public async Task<ActionResult<List<GastoPendienteDto>>> GetPendientes([FromQuery] int? month, [FromQuery] int? year)
    {
        int m = month ?? DateTime.Now.Month;
        int y = year ?? DateTime.Now.Year;
        return await _service.GetPendientesDelMesAsync(m, y);
    }

    /// <summary>
    /// Aplica un gasto recurrente como MovimientoCaja del mes indicado.
    /// Permite ajustar el monto real.
    /// </summary>
    [HttpPost("aplicar")]
    public async Task<IActionResult> AplicarGasto([FromBody] AplicarGastoRequest request)
    {
        try
        {
            int m = request.Month ?? DateTime.Now.Month;
            int y = request.Year ?? DateTime.Now.Year;

            await _service.AplicarGastoAsync(request.GastoRecurrenteId, m, y, request.MontoReal);
            await _auditService.RegistrarAccionAsync(
                User.Identity?.Name ?? "Desconocido",
                "Aplicar Gasto Recurrente",
                $"GastoRecurrenteId: {request.GastoRecurrenteId}, Mes: {m}/{y}, MontoReal: ${request.MontoReal?.ToString("N0") ?? "estimado"}");
            return Ok("Gasto registrado en caja.");
        }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        catch (Exception ex) { return BadRequest(ex.Message); }
    }

    /// <summary>
    /// Aplica todos los gastos recurrentes pendientes del mes con sus montos estimados.
    /// </summary>
    [HttpPost("aplicar-todos")]
    public async Task<IActionResult> AplicarTodos([FromBody] AplicarTodosRequest request)
    {
        int m = request.Month ?? DateTime.Now.Month;
        int y = request.Year ?? DateTime.Now.Year;

        var pendientes = await _service.GetPendientesDelMesAsync(m, y);
        int aplicados = 0;
        var errores = new List<string>();

        foreach (var p in pendientes)
        {
            try
            {
                // Buscar si hay monto personalizado en la request
                decimal? montoReal = request.MontosPersonalizados?
                    .FirstOrDefault(mp => mp.GastoRecurrenteId == p.GastoRecurrenteId)?.MontoReal;

                await _service.AplicarGastoAsync(p.GastoRecurrenteId, m, y, montoReal);
                aplicados++;
            }
            catch (Exception ex)
            {
                errores.Add($"{p.Descripcion}: {ex.Message}");
            }
        }

        string resultado = $"Aplicados: {aplicados}/{pendientes.Count}";
        if (errores.Any()) resultado += $". Errores: {string.Join("; ", errores)}";

        await _auditService.RegistrarAccionAsync(
            User.Identity?.Name ?? "Desconocido",
            "Aplicar Todos Gastos Recurrentes",
            $"Mes: {m}/{y}, {resultado}");

        return Ok(resultado);
    }
}

public class AplicarGastoRequest
{
    public int GastoRecurrenteId { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public decimal? MontoReal { get; set; } // Si null, usa MontoEstimado
}

public class AplicarTodosRequest
{
    public int? Month { get; set; }
    public int? Year { get; set; }
    public List<MontoPersonalizado>? MontosPersonalizados { get; set; }
}

public class MontoPersonalizado
{
    public int GastoRecurrenteId { get; set; }
    public decimal MontoReal { get; set; }
}
