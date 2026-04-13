using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoToMovimientoCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Cantidad",
                table: "MovimientosCaja",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductoId",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cantidad",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "ProductoId",
                table: "MovimientosCaja");
        }
    }
}
