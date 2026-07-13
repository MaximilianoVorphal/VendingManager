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
            // Esta migración hace cleanup de objetos que fueron creados por migraciones
            // intermedias (CapEx/EBITDA) que ya no existen en el modelo. Usamos SQL crudo
            // con IF EXISTS para que sea idempotente: no falla si los objetos nunca existieron
            // (fresh DB) ni si ya existen (DB con historia de migraciones aplicadas).

            migrationBuilder.Sql(@"
                -- Drop FK + index created by CapExEbitdaPerMachine (if they exist)
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_MovimientosCaja_Maquinas_MaquinaId' AND parent_object_id = OBJECT_ID('MovimientosCaja'))
                    ALTER TABLE [MovimientosCaja] DROP CONSTRAINT [FK_MovimientosCaja_Maquinas_MaquinaId];

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MovimientosCaja_MaquinaId' AND object_id = OBJECT_ID('MovimientosCaja'))
                    DROP INDEX [IX_MovimientosCaja_MaquinaId] ON [MovimientosCaja];

                -- Drop EBITDA tables (if they exist)
                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DepreciacionesMaquina')
                    DROP TABLE [DepreciacionesMaquina];

                IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DepreciacionesMaquinaHistory')
                    DROP TABLE [DepreciacionesMaquinaHistory];

                -- Drop EBITDA columns (if they exist)
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MovimientosCajaHistory') AND name = 'MaquinaId')
                    ALTER TABLE [MovimientosCajaHistory] DROP COLUMN [MaquinaId];

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MovimientosCaja') AND name = 'MaquinaId')
                    ALTER TABLE [MovimientosCaja] DROP COLUMN [MaquinaId];

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Maquinas') AND name = 'FechaBaja')
                    ALTER TABLE [Maquinas] DROP COLUMN [FechaBaja];

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Maquinas') AND name = 'FechaInstalacion')
                    ALTER TABLE [Maquinas] DROP COLUMN [FechaInstalacion];
            ");
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
