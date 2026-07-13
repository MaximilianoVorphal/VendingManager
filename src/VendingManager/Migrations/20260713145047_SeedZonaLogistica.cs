using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class SeedZonaLogistica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ZonasLogisticas",
                columns: new[] { "Id", "CostoBaseViaje", "Nombre" },
                values: new object[,]
                {
                    { 1, 25000m, "Zona Norte" },
                    { 2, 15000m, "Zona Centro" },
                    { 3, 20000m, "Zona Sur" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ZonasLogisticas",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ZonasLogisticas",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ZonasLogisticas",
                keyColumn: "Id",
                keyValue: 3);
        }
    }
}
