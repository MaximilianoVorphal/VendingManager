using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class MigrateActivoToTerminado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate Estado=1 (Activo) rows to Estado=2 (Terminado).
            // Idempotent: safe to run even if no Activo rows exist.
            migrationBuilder.Sql(
                "UPDATE TemplatesRecarga SET Estado = 2 WHERE Estado = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: restore Estado=1 for rows that were Activo.
            // Only updates rows that are currently Estado=2 — safe rollback.
            migrationBuilder.Sql(
                "UPDATE TemplatesRecarga SET Estado = 1 WHERE Estado = 2");
        }
    }
}