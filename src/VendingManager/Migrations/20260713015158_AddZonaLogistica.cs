using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddZonaLogistica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ZonaLogisticaId",
                table: "Maquinas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZonasLogisticas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CostoBaseViaje = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZonasLogisticas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Maquinas_ZonaLogisticaId",
                table: "Maquinas",
                column: "ZonaLogisticaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Maquinas_ZonasLogisticas_ZonaLogisticaId",
                table: "Maquinas",
                column: "ZonaLogisticaId",
                principalTable: "ZonasLogisticas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Maquinas_ZonasLogisticas_ZonaLogisticaId",
                table: "Maquinas");

            migrationBuilder.DropTable(
                name: "ZonasLogisticas");

            migrationBuilder.DropIndex(
                name: "IX_Maquinas_ZonaLogisticaId",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "ZonaLogisticaId",
                table: "Maquinas");
        }
    }
}
