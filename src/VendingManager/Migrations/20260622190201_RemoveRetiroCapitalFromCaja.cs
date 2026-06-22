using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRetiroCapitalFromCaja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullify MovimientoCajaId on Transferencias linked to RETIRO_CAPITAL movements
            // before deleting, to avoid FK constraint violations.
            migrationBuilder.Sql(@"
                UPDATE Transferencias
                SET MovimientoCajaId = NULL
                WHERE MovimientoCajaId IN (
                    SELECT Id FROM MovimientosCaja WHERE Categoria = 'RETIRO_CAPITAL'
                )
            ");

            migrationBuilder.Sql(@"
                DELETE FROM MovimientosCaja WHERE Categoria = 'RETIRO_CAPITAL'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // RETIRO_CAPITAL movements cannot be restored automatically.
        }
    }
}
