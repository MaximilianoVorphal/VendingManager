using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PeriodoId",
                table: "Transferencias",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Trabajador = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_PeriodoId",
                table: "Transferencias",
                column: "PeriodoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transferencias_AccountingPeriods_PeriodoId",
                table: "Transferencias",
                column: "PeriodoId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transferencias_AccountingPeriods_PeriodoId",
                table: "Transferencias");

            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropIndex(
                name: "IX_Transferencias_PeriodoId",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "PeriodoId",
                table: "Transferencias");
        }
    }
}
