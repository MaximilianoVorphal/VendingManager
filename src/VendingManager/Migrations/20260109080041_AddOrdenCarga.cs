using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdenCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrdenesCarga",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFinalizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaquinaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdenesCarga", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DetallesOrdenCarga",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenCargaId = table.Column<int>(type: "int", nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: false),
                    ProductoNombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CantidadSolicitada = table.Column<int>(type: "int", nullable: false),
                    CantidadRetornada = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DetallesOrdenCarga", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DetallesOrdenCarga_OrdenesCarga_OrdenCargaId",
                        column: x => x.OrdenCargaId,
                        principalTable: "OrdenesCarga",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DetallesOrdenCarga_OrdenCargaId",
                table: "DetallesOrdenCarga",
                column: "OrdenCargaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DetallesOrdenCarga");

            migrationBuilder.DropTable(
                name: "OrdenesCarga");
        }
    }
}
