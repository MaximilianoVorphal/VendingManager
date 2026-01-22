using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SnapshotSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PeriodoRecargaId = table.Column<int>(type: "int", nullable: false),
                    NumeroSlot = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ProductoId = table.Column<int>(type: "int", nullable: true),
                    CantidadInicial = table.Column<int>(type: "int", nullable: false),
                    CapacidadSlot = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SnapshotSlots_PeriodosRecarga_PeriodoRecargaId",
                        column: x => x.PeriodoRecargaId,
                        principalTable: "PeriodosRecarga",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SnapshotSlots_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotSlots_PeriodoRecargaId",
                table: "SnapshotSlots",
                column: "PeriodoRecargaId");

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotSlots_ProductoId",
                table: "SnapshotSlots",
                column: "ProductoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnapshotSlots");
        }
    }
}
