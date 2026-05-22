using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddActivoState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaCargaFin",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "FechaCargaInicio",
                table: "TemplatesRecarga");

            migrationBuilder.AlterColumn<int>(
                name: "Estado",
                table: "TemplatesRecarga",
                type: "int",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValueSql: "2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Estado",
                table: "TemplatesRecarga",
                type: "int",
                nullable: false,
                defaultValueSql: "2",
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValueSql: "0");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCargaFin",
                table: "TemplatesRecarga",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCargaInicio",
                table: "TemplatesRecarga",
                type: "datetime2",
                nullable: true);
        }
    }
}
