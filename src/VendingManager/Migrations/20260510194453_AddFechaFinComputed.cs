using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddFechaFinComputed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 2: Add PERSISTED computed column FechaFin
            // This column is derived from the next period's FechaRecarga for the same machine.
            // If no next period exists, the end date is 2099-12-31 23:59:59.9999999 (sentinel).
            migrationBuilder.Sql(@"
                ALTER TABLE PeriodosRecarga
                ADD FechaFin AS (
                    ISNULL(
                        (SELECT TOP 1 FechaRecarga
                         FROM PeriodosRecarga p2
                         WHERE p2.MaquinaId = PeriodosRecarga.MaquinaId
                           AND p2.FechaRecarga > PeriodosRecarga.FechaRecarga
                         ORDER BY p2.FechaRecarga),
                        CAST('2099-12-31 23:59:59.9999999' AS datetime2)
                    )
                ) PERSISTED;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: SQL Server does not support DROP COLUMN for computed columns directly
            // in all versions; the column will be dropped with the table.
            // Down migration is only supported through phase 2 — phase 3 down is intentionally blocked.
        }
    }
}
