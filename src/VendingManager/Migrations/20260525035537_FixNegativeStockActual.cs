using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class FixNegativeStockActual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sql = @"
DECLARE @FixedCount int = 0;

-- Reset negative StockActual to 0 (fallback) or recalculate from latest Terminado template
UPDATE cs
SET cs.StockActual = COALESCE(
    (
        SELECT TOP 1 ss.CantidadInicial
        FROM TemplatesRecarga t
        INNER JOIN PeriodosRecarga pr ON pr.TemplateRecargaId = t.Id
        INNER JOIN SnapshotSlots ss ON ss.PeriodoRecargaId = pr.Id
        WHERE t.Estado = 2  -- EstadoTemplate.Terminado
          AND pr.MaquinaId = cs.MaquinaId
          AND ss.NumeroSlot = cs.NumeroSlot
        ORDER BY t.FechaCreacion DESC
    ),
    0
)
FROM ConfiguracionSlots cs
WHERE cs.StockActual < 0;

SET @FixedCount = @@ROWCOUNT;

IF @FixedCount > 0
    PRINT 'Fixed ' + CAST(@FixedCount AS nvarchar(10)) + ' negative StockActual values';
ELSE
    PRINT 'No negative StockActual values found — nothing to fix';
";
            migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: data repair migration, no forward-safe rollback
        }
    }
}