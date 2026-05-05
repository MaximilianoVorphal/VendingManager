using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditoriaJsonColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AfterJson",
                table: "Auditoria",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeJson",
                table: "Auditoria",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntityId",
                table: "Auditoria",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "Auditoria",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AfterJson",
                table: "Auditoria");

            migrationBuilder.DropColumn(
                name: "BeforeJson",
                table: "Auditoria");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "Auditoria");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "Auditoria");
        }
    }
}
