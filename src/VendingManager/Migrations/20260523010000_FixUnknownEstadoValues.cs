using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class FixUnknownEstadoValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pone cualquier valor de Estado que no sea Pendiente(0) o Terminado(2)
            // como Terminado(2). Idempotente: si no hay rows afectadas, no hace nada.
            migrationBuilder.Sql(
                "UPDATE TemplatesRecarga SET Estado = 2 WHERE Estado NOT IN (0, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback no posible: no podemos saber qué valor tenían antes.
            // Intencionalmente vacío — esta migración es correctiva.
        }
    }
}
