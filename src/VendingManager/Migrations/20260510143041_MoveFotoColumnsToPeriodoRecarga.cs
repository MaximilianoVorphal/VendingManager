using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class MoveFotoColumnsToPeriodoRecarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add columns to PeriodosRecarga first (safer order — ADD before DROP)
            migrationBuilder.AddColumn<byte[]>(
                name: "FotoGuia",
                table: "PeriodosRecarga",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoOcr",
                table: "PeriodosRecarga",
                type: "varbinary(max)",
                nullable: true);

            // Then drop from TemplatesRecarga (existing photo data intentionally not migrated)
            migrationBuilder.DropColumn(
                name: "FotoGuia",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "FotoOcr",
                table: "TemplatesRecarga");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoGuia",
                table: "PeriodosRecarga");

            migrationBuilder.DropColumn(
                name: "FotoOcr",
                table: "PeriodosRecarga");

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoGuia",
                table: "TemplatesRecarga",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoOcr",
                table: "TemplatesRecarga",
                type: "varbinary(max)",
                nullable: true);
        }
    }
}
