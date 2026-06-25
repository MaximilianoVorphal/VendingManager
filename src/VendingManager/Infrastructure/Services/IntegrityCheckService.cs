using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class IntegrityCheckService : IIntegrityCheckService
{
    private readonly ApplicationDbContext _context;

    // Categorías estructurales que no son gastos reales (refleja ContabilidadService.EsGastoReal)
    private static readonly HashSet<string> CategoriasEstructurales = new(StringComparer.OrdinalIgnoreCase)
    {
        "RETIRO_CAPITAL",
        "DEVOLUCION_RENDICION"
    };

    public IntegrityCheckService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<IntegrityCheckResultDto>> RunAllChecksAsync(CancellationToken ct = default)
    {
        var results = new List<IntegrityCheckResultDto>
        {
            await Check3aOverAllocatedTransfersAsync(ct),
            await Check3bOrphanComprasAsync(ct),
            await Check3cAutoConciliatedWithoutComprasAsync(ct),
            await Check7aClosedRendicionNonZeroSaldoAsync(ct),
            await Check7bCrossLinkedTransferenciasAsync(ct),
            await Check7cOpenRendicionNegativeSaldoAsync(ct)
        };

        return results.Where(r => r.DetailEntries.Count > 0).ToList();
    }

    // ───────────────────────────────────────────────────────────────
    // Check #3 sub-check A: Over-allocated transfers
    //   Error entries where Sum(Compras.MontoTotal) > Transferencia.Monto
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check3aOverAllocatedTransfersAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "3A - Transferencia sobre-asignada",
            Severity = CheckSeverity.Error,
            Timestamp = DateTime.UtcNow
        };

        var transferencias = await _context.Transferencias
            .Include(t => t.Compras)
            .Where(t => t.Compras.Any())
            .ToListAsync(ct);

        foreach (var t in transferencias)
        {
            var totalCompras = t.Compras.Sum(c => c.MontoTotal);
            if (totalCompras > t.Monto)
            {
                result.DetailEntries.Add(new IntegrityCheckDetailDto
                {
                    TransferenciaId = t.Id,
                    Trabajador = t.Trabajador,
                    MontoEntregado = t.Monto,
                    MontoTotal = totalCompras,
                    Diferencia = t.Monto - totalCompras,
                    Mensaje = $"Transferencia #{t.Id} por ${t.Monto:N2} tiene compras por ${totalCompras:N2} (excede en ${totalCompras - t.Monto:N2})"
                });
            }
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // Check #3 sub-check B: Orphan Compras (TransferenciaId IS NULL)
    //   Warn entries
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check3bOrphanComprasAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "3B - Compras huérfanas",
            Severity = CheckSeverity.Warn,
            Timestamp = DateTime.UtcNow
        };

        var orphanCompras = await _context.Compras
            .Where(c => c.TransferenciaId == null)
            .ToListAsync(ct);

        foreach (var c in orphanCompras)
        {
            result.DetailEntries.Add(new IntegrityCheckDetailDto
            {
                CompraId = c.Id,
                Trabajador = c.Trabajador,
                MontoTotal = c.MontoTotal,
                Mensaje = $"Compra #{c.Id} por ${c.MontoTotal:N2} ({c.Proveedor}) no está vinculada a ninguna transferencia"
            });
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // Check #3 sub-check C: Auto-conciliated transfers without Compras
    //   Info entries (valid in period-close, but worth noting)
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check3cAutoConciliatedWithoutComprasAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "3C - Transferencias conciliadas sin compras",
            Severity = CheckSeverity.Info,
            Timestamp = DateTime.UtcNow
        };

        var conciliadasSinCompras = await _context.Transferencias
            .Include(t => t.Compras)
            .Where(t => t.Estado == TransferenciaEstado.Conciliado && !t.Compras.Any())
            .ToListAsync(ct);

        foreach (var t in conciliadasSinCompras)
        {
            result.DetailEntries.Add(new IntegrityCheckDetailDto
            {
                TransferenciaId = t.Id,
                Trabajador = t.Trabajador,
                MontoEntregado = t.Monto,
                Mensaje = $"Transferencia #{t.Id} por ${t.Monto:N2} está conciliada pero no tiene compras vinculadas"
            });
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // Check #7 sub-check A: Closed Rendicion with non-zero SaldoADevolver
    //   Error entries
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check7aClosedRendicionNonZeroSaldoAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "7A - Rendiciones cerradas con saldo pendiente",
            Severity = CheckSeverity.Error,
            Timestamp = DateTime.UtcNow
        };

        var closedRendiciones = await _context.Rendiciones
            .Where(r => r.Estado == RendicionEstado.Cerrada)
            .Include(r => r.Transferencias)
                .ThenInclude(t => t.Compras)
            .Include(r => r.Gastos)
            .ToListAsync(ct);

        // Fetch Devoluciones for these rendiciones in a single query
        var rendicionIds = closedRendiciones.Select(r => r.Id).ToList();
        // Materialize before GroupBy: the EF Core provider cannot translate a bare
        // GroupBy (not composed into an aggregate projection). The filtered set is small
        // (devoluciones for the rendiciones in scope), so client-side grouping is fine.
        var devolucionesByRendicion = (await _context.Devoluciones
            .Where(d => d.RendicionId != null && rendicionIds.Contains(d.RendicionId!.Value))
            .ToListAsync(ct))
            .GroupBy(d => d.RendicionId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Monto));

        foreach (var r in closedRendiciones)
        {
            var totalTransferido = r.Transferencias.Sum(t => t.Monto);
            var totalCompras = r.Transferencias.SelectMany(t => t.Compras).Sum(c => c.MontoTotal);
            var totalGastos = r.Gastos
                .Where(EsGastoReal)
                .Sum(g => Math.Abs(g.Monto));
            var devuelto = devolucionesByRendicion.GetValueOrDefault(r.Id, 0m);
            var saldoADevolver = totalTransferido - totalCompras - totalGastos - devuelto;

            if (saldoADevolver != 0)
            {
                result.DetailEntries.Add(new IntegrityCheckDetailDto
                {
                    RendicionId = r.Id,
                    Trabajador = r.Trabajador,
                    MontoEntregado = totalTransferido,
                    MontoRecibido = totalCompras + totalGastos + devuelto,
                    SaldoADevolver = saldoADevolver,
                    Mensaje = $"Rendición #{r.Id} ({r.Trabajador}) está cerrada con saldo pendiente de ${saldoADevolver:N2}"
                });
            }
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // Check #7 sub-check B: Cross-linked Transferencia
    //   Warn entries — both RendicionId AND PeriodoId non-null
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check7bCrossLinkedTransferenciasAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "7B - Transferencias con doble vinculación",
            Severity = CheckSeverity.Warn,
            Timestamp = DateTime.UtcNow
        };

        var crossLinked = await _context.Transferencias
            .Where(t => t.RendicionId != null && t.PeriodoId != null)
            .ToListAsync(ct);

        foreach (var t in crossLinked)
        {
            result.DetailEntries.Add(new IntegrityCheckDetailDto
            {
                TransferenciaId = t.Id,
                Trabajador = t.Trabajador,
                MontoEntregado = t.Monto,
                Mensaje = $"Transferencia #{t.Id} por ${t.Monto:N2} tiene RendicionId={t.RendicionId} Y PeriodoId={t.PeriodoId}"
            });
        }

        return result;
    }

    // ───────────────────────────────────────────────────────────────
    // Check #7 sub-check C: Open Rendicion with negative SaldoADevolver
    //   Error entries
    // ───────────────────────────────────────────────────────────────
    private async Task<IntegrityCheckResultDto> Check7cOpenRendicionNegativeSaldoAsync(CancellationToken ct)
    {
        var result = new IntegrityCheckResultDto
        {
            CheckType = "7C - Rendiciones abiertas con saldo negativo",
            Severity = CheckSeverity.Error,
            Timestamp = DateTime.UtcNow
        };

        var openRendiciones = await _context.Rendiciones
            .Where(r => r.Estado == RendicionEstado.Abierta)
            .Include(r => r.Transferencias)
                .ThenInclude(t => t.Compras)
            .Include(r => r.Gastos)
            .ToListAsync(ct);

        var rendicionIds = openRendiciones.Select(r => r.Id).ToList();
        // Materialize before GroupBy: the EF Core provider cannot translate a bare
        // GroupBy (not composed into an aggregate projection). The filtered set is small
        // (devoluciones for the rendiciones in scope), so client-side grouping is fine.
        var devolucionesByRendicion = (await _context.Devoluciones
            .Where(d => d.RendicionId != null && rendicionIds.Contains(d.RendicionId!.Value))
            .ToListAsync(ct))
            .GroupBy(d => d.RendicionId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Monto));

        foreach (var r in openRendiciones)
        {
            var totalTransferido = r.Transferencias.Sum(t => t.Monto);
            var totalCompras = r.Transferencias.SelectMany(t => t.Compras).Sum(c => c.MontoTotal);
            var totalGastos = r.Gastos
                .Where(EsGastoReal)
                .Sum(g => Math.Abs(g.Monto));
            var devuelto = devolucionesByRendicion.GetValueOrDefault(r.Id, 0m);
            var saldoADevolver = totalTransferido - totalCompras - totalGastos - devuelto;

            if (saldoADevolver < 0)
            {
                result.DetailEntries.Add(new IntegrityCheckDetailDto
                {
                    RendicionId = r.Id,
                    Trabajador = r.Trabajador,
                    MontoEntregado = totalTransferido,
                    MontoRecibido = totalCompras + totalGastos + devuelto,
                    SaldoADevolver = saldoADevolver,
                    Mensaje = $"Rendición #{r.Id} ({r.Trabajador}) está abierta con saldo negativo de ${saldoADevolver:N2}"
                });
            }
        }

        return result;
    }

    private static bool EsGastoReal(MovimientoCaja m) =>
        !CategoriasEstructurales.Contains(m.Categoria ?? string.Empty);
}
