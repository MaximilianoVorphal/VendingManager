using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddFechaRecargaColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 1: Add FechaRecarga column (nullable first for backfill safety)
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRecarga",
                table: "PeriodosRecarga",
                type: "datetime2",
                nullable: true);

            // Backfill: copy FechaInicio values into FechaRecarga
            migrationBuilder.Sql(@"
                UPDATE PeriodosRecarga
                SET FechaRecarga = FechaInicio;
            ");

            // Make NOT NULL now that all rows are populated
            migrationBuilder.AlterColumn<DateTime>(
                name: "FechaRecarga",
                table: "PeriodosRecarga",
                type: "datetime2",
                nullable: false);

            // Unique index: prevents duplicate FechaRecarga per machine (chain integrity)
            migrationBuilder.CreateIndex(
                name: "IX_PeriodosRecarga_MaquinaId_FechaRecarga",
                table: "PeriodosRecarga",
                columns: new[] { "MaquinaId", "FechaRecarga" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PeriodosRecarga_MaquinaId_FechaRecarga",
                table: "PeriodosRecarga");

            migrationBuilder.DropColumn(
                name: "FechaRecarga",
                table: "PeriodosRecarga");
        }
    }
}
