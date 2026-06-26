using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddProveedorCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProveedorCatalogId",
                table: "Compras",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProveedorCatalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NombreCanonical = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProveedorCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProveedorCatalogHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NombreCanonical = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProveedorCatalogHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProveedorAlias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RawNameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProveedorCatalogId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProveedorAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProveedorAlias_ProveedorCatalog_ProveedorCatalogId",
                        column: x => x.ProveedorCatalogId,
                        principalTable: "ProveedorCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Compras_ProveedorCatalogId",
                table: "Compras",
                column: "ProveedorCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_ProveedorAlias_ProveedorCatalogId",
                table: "ProveedorAlias",
                column: "ProveedorCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_ProveedorAlias_RawNameNormalized",
                table: "ProveedorAlias",
                column: "RawNameNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProveedorCatalog_NombreCanonical",
                table: "ProveedorCatalog",
                column: "NombreCanonical",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Compras_ProveedorCatalog_ProveedorCatalogId",
                table: "Compras",
                column: "ProveedorCatalogId",
                principalTable: "ProveedorCatalog",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Compras_ProveedorCatalog_ProveedorCatalogId",
                table: "Compras");

            migrationBuilder.DropTable(
                name: "ProveedorAlias");

            migrationBuilder.DropTable(
                name: "ProveedorCatalogHistory");

            migrationBuilder.DropTable(
                name: "ProveedorCatalog");

            migrationBuilder.DropIndex(
                name: "IX_Compras_ProveedorCatalogId",
                table: "Compras");

            migrationBuilder.DropColumn(
                name: "ProveedorCatalogId",
                table: "Compras");
        }
    }
}
