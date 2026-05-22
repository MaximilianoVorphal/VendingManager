using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddEstadoTemplateYRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Estado",
                table: "TemplatesRecarga",
                type: "int",
                nullable: false,
                defaultValueSql: "2");

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

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "TemplatesRecarga",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estado",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "FechaCargaFin",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "FechaCargaInicio",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TemplatesRecarga");
        }
    }
}
