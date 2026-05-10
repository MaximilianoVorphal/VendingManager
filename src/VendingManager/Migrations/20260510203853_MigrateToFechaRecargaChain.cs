using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class MigrateToFechaRecargaChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop old stored FechaFin constraint + column
            // EF Core's DropColumn handles the default constraint drop automatically

            // 2. Drop old stored FechaFin column
            migrationBuilder.DropColumn(
                name: "FechaFin",
                table: "PeriodosRecarga");

            // 3. Rename FechaInicio → FechaRecarga
            migrationBuilder.RenameColumn(
                name: "FechaInicio",
                table: "PeriodosRecarga",
                newName: "FechaRecarga");

            // 4. Unique index: prevents duplicate FechaRecarga per machine (chain integrity)
            migrationBuilder.CreateIndex(
                name: "IX_PeriodosRecarga_MaquinaId_FechaRecarga",
                table: "PeriodosRecarga",
                columns: new[] { "MaquinaId", "FechaRecarga" },
                unique: true);

            // 5. Scalar function for chain end-date (subqueries not allowed in computed columns)
            migrationBuilder.Sql(@"
                CREATE FUNCTION dbo.GetPeriodoFechaFin(@MaquinaId int, @FechaRecarga datetime2)
                RETURNS datetime2
                AS
                BEGIN
                    DECLARE @next datetime2;
                    SELECT TOP 1 @next = FechaRecarga
                    FROM PeriodosRecarga
                    WHERE MaquinaId = @MaquinaId AND FechaRecarga > @FechaRecarga
                    ORDER BY FechaRecarga;
                    RETURN ISNULL(@next, CAST('2099-12-31 23:59:59.9999999' AS datetime2));
                END;
            ");

            // 6. Add computed column using the scalar function
            migrationBuilder.Sql(@"
                ALTER TABLE PeriodosRecarga
                ADD FechaFin AS dbo.GetPeriodoFechaFin(MaquinaId, FechaRecarga);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE PeriodosRecarga DROP COLUMN FechaFin;");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS dbo.GetPeriodoFechaFin;");

            migrationBuilder.DropIndex(
                name: "IX_PeriodosRecarga_MaquinaId_FechaRecarga",
                table: "PeriodosRecarga");

            migrationBuilder.RenameColumn(
                name: "FechaRecarga",
                table: "PeriodosRecarga",
                newName: "FechaInicio");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaFin",
                table: "PeriodosRecarga",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2099, 12, 31, 23, 59, 59, 999,
                    DateTimeKind.Unspecified));
        }
    }
}
