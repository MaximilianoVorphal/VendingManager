using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AllowNullProductInSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConfiguracionSlots_Productos_ProductoId",
                table: "ConfiguracionSlots");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.AlterColumn<int>(
                name: "ProductoId",
                table: "ConfiguracionSlots",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_ConfiguracionSlots_Productos_ProductoId",
                table: "ConfiguracionSlots",
                column: "ProductoId",
                principalTable: "Productos",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConfiguracionSlots_Productos_ProductoId",
                table: "ConfiguracionSlots");

            migrationBuilder.AlterColumn<int>(
                name: "ProductoId",
                table: "ConfiguracionSlots",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_ConfiguracionSlots_Productos_ProductoId",
                table: "ConfiguracionSlots",
                column: "ProductoId",
                principalTable: "Productos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
