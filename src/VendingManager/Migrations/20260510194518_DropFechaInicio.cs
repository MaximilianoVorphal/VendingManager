using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class DropFechaInicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 3: Remove the old stored columns
            // Note: FechaFin was already replaced by the computed column in Phase 2,
            // but the original column was NOT dropped in Phase 2 — SQL Server keeps
            // the original FechaFin column AND adds the computed column with a different name
            // if we ADD without dropping first. We need to explicitly drop the original stored column.
            migrationBuilder.DropColumn(
                name: "FechaInicio",
                table: "PeriodosRecarga");

            // Drop the old stored FechaFin column (replaced by computed column in phase 2)
            migrationBuilder.DropColumn(
                name: "FechaFin",
                table: "PeriodosRecarga");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // NOT SUPPORTED — requires restoring from backup or dual-write fallback.
            // Phase 3 down migration is intentionally blocked.
            throw new InvalidOperationException(
                "Phase 3 down migration is not supported. This requires restoring from a database backup.");
        }
    }
}
