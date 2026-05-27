using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddProductoEAN : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "EsPendiente",
                table: "DetallesCompra",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProductoEAN",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EAN = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    SKU = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    Proveedor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PackSize = table.Column<int>(type: "int", nullable: true),
                    DescripcionProveedor = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductoEAN", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductoEAN_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductoEAN_EAN",
                table: "ProductoEAN",
                column: "EAN",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductoEAN_ProductoId",
                table: "ProductoEAN",
                column: "ProductoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductoEAN");

            migrationBuilder.AlterColumn<bool>(
                name: "EsPendiente",
                table: "DetallesCompra",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");
        }
    }
}
