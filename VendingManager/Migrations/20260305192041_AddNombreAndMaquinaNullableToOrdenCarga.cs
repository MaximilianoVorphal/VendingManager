using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddNombreAndMaquinaNullableToOrdenCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nombre",
                table: "OrdenesCarga",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MaquinaId",
                table: "OrdenesCarga",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nombre",
                table: "OrdenesCarga");

            migrationBuilder.AlterColumn<int>(
                name: "MaquinaId",
                table: "OrdenesCarga",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
