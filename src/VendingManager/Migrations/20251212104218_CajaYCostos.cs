using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class CajaYCostos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "MovimientosCaja",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "MovimientosCaja");
        }
    }
}
