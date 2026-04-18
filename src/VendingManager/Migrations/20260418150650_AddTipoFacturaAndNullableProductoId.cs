using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoFacturaAndNullableProductoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DetallesCompra_Productos_ProductoId",
                table: "DetallesCompra");

            migrationBuilder.AlterColumn<int>(
                name: "ProductoId",
                table: "DetallesCompra",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "DescripcionItem",
                table: "DetallesCompra",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoFactura",
                table: "Compras",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_DetallesCompra_Productos_ProductoId",
                table: "DetallesCompra",
                column: "ProductoId",
                principalTable: "Productos",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DetallesCompra_Productos_ProductoId",
                table: "DetallesCompra");

            migrationBuilder.DropColumn(
                name: "DescripcionItem",
                table: "DetallesCompra");

            migrationBuilder.DropColumn(
                name: "TipoFactura",
                table: "Compras");

            migrationBuilder.AlterColumn<int>(
                name: "ProductoId",
                table: "DetallesCompra",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DetallesCompra_Productos_ProductoId",
                table: "DetallesCompra",
                column: "ProductoId",
                principalTable: "Productos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
