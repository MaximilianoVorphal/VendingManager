using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class BackfillProductoCostos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO ProductoCostos (ProductoId, Costo, FechaDesde, FechaHasta)
                SELECT
                    dc.ProductoId,
                    dc.CostoUnitario,
                    c.FechaCompra AS FechaDesde,
                    LEAD(c.FechaCompra) OVER (PARTITION BY dc.ProductoId ORDER BY c.FechaCompra) AS FechaHasta
                FROM DetallesCompra dc
                INNER JOIN Compras c ON dc.CompraId = c.Id
                WHERE dc.ProductoId IS NOT NULL
                ORDER BY dc.ProductoId, c.FechaCompra");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ProductoCostos");
        }
    }
}
