using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class CapExEbitdaPerMachine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaquinaId",
                table: "MovimientosCajaHistory",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaquinaId",
                table: "MovimientosCaja",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaBaja",
                table: "Maquinas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaInstalacion",
                table: "Maquinas",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

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

            // ── Seed data: DepreciacionesMaquina + GastoRecurrente per-machine ──

            // DepreciacionMaquina: one row per existing machine with placeholder values
            migrationBuilder.Sql(@"
                INSERT INTO DepreciacionesMaquina (MaquinaId, Descripcion, ValorAdquisicion, ValorResidual, VidaUtilMeses, FechaAdquisicion, MetodoDepreciacion, Activo, FechaCreacion)
                SELECT m.Id, N'Máquina expendedora', 2000000.00, 200000.00, 60, '20250101', N'LINEAL', 1, GETDATE()
                FROM Maquinas m
                WHERE NOT EXISTS (SELECT 1 FROM DepreciacionesMaquina WHERE MaquinaId = m.Id);
            ");

            // GastoRecurrente: WOM internet per machine, fleet $30K split evenly
            migrationBuilder.Sql(@"
                DECLARE @cnt INT = (SELECT COUNT(*) FROM Maquinas);
                DECLARE @share decimal(18,2) = IIF(@cnt > 0, 30000.00 / @cnt, 30000.00);

                INSERT INTO GastosRecurrentes (Descripcion, MontoEstimado, Categoria, Tipo, Activo, MaquinaId, FechaCreacion)
                SELECT CONCAT('Internet WOM M-', m.Id), @share, 'INTERNET', 'GASTO', 1, m.Id, GETDATE()
                FROM Maquinas m
                WHERE NOT EXISTS (
                    SELECT 1 FROM GastosRecurrentes
                    WHERE MaquinaId = m.Id AND Categoria = 'INTERNET' AND Descripcion LIKE 'Internet WOM%'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovimientosCaja_Maquinas_MaquinaId",
                table: "MovimientosCaja");

            migrationBuilder.DropTable(
                name: "DepreciacionesMaquina");

            migrationBuilder.DropTable(
                name: "DepreciacionesMaquinaHistory");

            migrationBuilder.DropIndex(
                name: "IX_MovimientosCaja_MaquinaId",
                table: "MovimientosCaja");

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
    }
}
