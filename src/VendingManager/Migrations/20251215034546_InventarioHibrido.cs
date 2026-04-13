using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class InventarioHibrido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Costo",
                table: "Productos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Costo",
                table: "Productos",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
