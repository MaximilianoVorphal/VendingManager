using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddComprobanteVarbinary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComprobanteImagenPath",
                table: "Devoluciones");

            migrationBuilder.AddColumn<byte[]>(
                name: "ComprobanteImagen",
                table: "Transferencias",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprobanteImagenContentType",
                table: "Transferencias",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprobanteImagenFileName",
                table: "Transferencias",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComprobanteImagen",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "ComprobanteImagenContentType",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "ComprobanteImagenFileName",
                table: "Transferencias");

            migrationBuilder.AddColumn<string>(
                name: "ComprobanteImagenPath",
                table: "Devoluciones",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
