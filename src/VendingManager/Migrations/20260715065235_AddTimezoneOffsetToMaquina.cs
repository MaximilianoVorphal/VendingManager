using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezoneOffsetToMaquina : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimezoneOffsetHours",
                table: "Maquinas",
                type: "int",
                nullable: true);

            // Backfill: machine "2410280012" is in a different timezone (+1 instead of -11).
            // All other machines default to Chilean CLT (-11).
            migrationBuilder.Sql(
                "UPDATE Maquinas SET TimezoneOffsetHours = 1 WHERE IdInternoMaquina = '2410280012'");
            migrationBuilder.Sql(
                "UPDATE Maquinas SET TimezoneOffsetHours = -11 WHERE TimezoneOffsetHours IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimezoneOffsetHours",
                table: "Maquinas");
        }
    }
}
