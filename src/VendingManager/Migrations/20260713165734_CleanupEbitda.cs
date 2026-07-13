using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class CleanupEbitda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK + index created by CapExEbitdaPerMachine
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosCaja_Maquinas_MaquinaId",
                table: "MovimientosCaja");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosCaja_MaquinaId",
                table: "MovimientosCaja");

            // Drop EBITDA tables
            migrationBuilder.DropTable(
                name: "DepreciacionesMaquina");

            migrationBuilder.DropTable(
                name: "DepreciacionesMaquinaHistory");

            // Drop EBITDA columns
            migrationBuilder.DropColumn(
                name: "MaquinaId",
                table: "MovimientosCajaHistory");

            migrationBuilder.DropColumn(
                name: "MaquinaId",
                table: "MovimientosCaja");

            migrationBuilder.DropColumn(
                name: "FechaBaja",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "FechaInstalacion",
                table: "Maquinas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: restore all EBITDA schema (no seed data re-insertion)
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInstalacion",
                table: "Maquinas",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaBaja",
                table: "Maquinas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaquinaId",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaquinaId",
                table: "MovimientosCajaHistory",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DepreciacionesMaquina",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaquinaId = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValorAdquisicion = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorResidual = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VidaUtilMeses = table.Column<int>(type: "int", nullable: false),
                    FechaAdquisicion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MetodoDepreciacion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepreciacionesMaquina", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepreciacionesMaquinaHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaquinaId = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ValorAdquisicion = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ValorResidual = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VidaUtilMeses = table.Column<int>(type: "int", nullable: false),
                    FechaAdquisicion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MetodoDepreciacion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepreciacionesMaquinaHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_MaquinaId",
                table: "MovimientosCaja",
                column: "MaquinaId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovimientosCaja_Maquinas_MaquinaId",
                table: "MovimientosCaja",
                column: "MaquinaId",
                principalTable: "Maquinas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
