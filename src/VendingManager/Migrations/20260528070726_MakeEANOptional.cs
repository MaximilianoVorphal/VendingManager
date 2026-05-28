using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class MakeEANOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductoEAN_EAN",
                table: "ProductoEAN");

            migrationBuilder.AlterColumn<string>(
                name: "EAN",
                table: "ProductoEAN",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(13)",
                oldMaxLength: 13);

            migrationBuilder.CreateIndex(
                name: "IX_ProductoEAN_EAN",
                table: "ProductoEAN",
                column: "EAN",
                unique: true,
                filter: "[EAN] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductoEAN_EAN",
                table: "ProductoEAN");

            migrationBuilder.AlterColumn<string>(
                name: "EAN",
                table: "ProductoEAN",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(13)",
                oldMaxLength: 13,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductoEAN_EAN",
                table: "ProductoEAN",
                column: "EAN",
                unique: true);
        }
    }
}
