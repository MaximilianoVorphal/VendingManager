using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class CollapseCuadrePeriodoFechaFin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Collapse legacy cuadre periods to a single-day window.
            // A cuadre period is 1:1 with a Transferencia (PeriodoId FK); its FechaFin
            // used to auto-expand one month. Children/totals are gathered by FK, not by
            // date range, so collapsing FechaFin to FechaInicio is safe.
            migrationBuilder.Sql(@"
                UPDATE p
                SET p.FechaFin = p.FechaInicio
                FROM AccountingPeriods p
                WHERE p.FechaFin <> p.FechaInicio
                  AND EXISTS (
                      SELECT 1 FROM Transferencias t WHERE t.PeriodoId = p.Id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: original month-long FechaFin values are not recoverable.
        }
    }
}
