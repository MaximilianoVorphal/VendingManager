using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddDevolucionAndVerificada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComprobanteImagenPath",
                table: "Transferencias",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Verificada",
                table: "Transferencias",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Verificada",
                table: "Compras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Devoluciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Trabajador = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RendicionId = table.Column<int>(type: "int", nullable: true),
                    PeriodoId = table.Column<int>(type: "int", nullable: true),
                    MovimientoCajaId = table.Column<int>(type: "int", nullable: true),
                    ComprobanteImagenPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devoluciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devoluciones_AccountingPeriods_PeriodoId",
                        column: x => x.PeriodoId,
                        principalTable: "AccountingPeriods",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Devoluciones_MovimientosCaja_MovimientoCajaId",
                        column: x => x.MovimientoCajaId,
                        principalTable: "MovimientosCaja",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Devoluciones_Rendiciones_RendicionId",
                        column: x => x.RendicionId,
                        principalTable: "Rendiciones",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_MovimientoCajaId",
                table: "Devoluciones",
                column: "MovimientoCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_PeriodoId",
                table: "Devoluciones",
                column: "PeriodoId");

            migrationBuilder.CreateIndex(
                name: "IX_Devoluciones_RendicionId",
                table: "Devoluciones",
                column: "RendicionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Devoluciones");

            migrationBuilder.DropColumn(
                name: "ComprobanteImagenPath",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "Verificada",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "Verificada",
                table: "Compras");
        }
    }
}
