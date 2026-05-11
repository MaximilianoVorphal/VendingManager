using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePeriodoFechaFinSentinel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR ALTER FUNCTION dbo.GetPeriodoFechaFin(@MaquinaId int, @FechaRecarga datetime2)
                RETURNS datetime2
                AS
                BEGIN
                    DECLARE @next datetime2;
                    SELECT TOP 1 @next = FechaRecarga
                    FROM dbo.PeriodosRecarga
                    WHERE MaquinaId = @MaquinaId AND FechaRecarga > @FechaRecarga
                    ORDER BY FechaRecarga;
                    RETURN ISNULL(@next,
                        CASE WHEN @FechaRecarga <= SYSDATETIME()
                             THEN SYSDATETIME()
                             ELSE CAST('2099-12-31 23:59:59.9999999' AS datetime2)
                        END);
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR ALTER FUNCTION dbo.GetPeriodoFechaFin(@MaquinaId int, @FechaRecarga datetime2)
                RETURNS datetime2
                AS
                BEGIN
                    DECLARE @next datetime2;
                    SELECT TOP 1 @next = FechaRecarga
                    FROM dbo.PeriodosRecarga
                    WHERE MaquinaId = @MaquinaId AND FechaRecarga > @FechaRecarga
                    ORDER BY FechaRecarga;
                    RETURN ISNULL(@next, CAST('2099-12-31 23:59:59.9999999' AS datetime2));
                END;
            ");
        }
    }
}