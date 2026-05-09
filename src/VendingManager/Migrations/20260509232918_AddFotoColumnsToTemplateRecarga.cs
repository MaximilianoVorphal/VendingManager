using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddFotoColumnsToTemplateRecarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "FotoGuia",
                table: "TemplatesRecarga",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FotoOcr",
                table: "TemplatesRecarga",
                type: "varbinary(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FotoGuia",
                table: "TemplatesRecarga");

            migrationBuilder.DropColumn(
                name: "FotoOcr",
                table: "TemplatesRecarga");
        }
    }
}
