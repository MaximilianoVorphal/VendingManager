using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendingManager.Migrations
{
    /// <inheritdoc />
    public partial class SeedAdminUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Inserta el usuario admin por defecto si no existe.
            // El hash BCrypt corresponde a la contraseña "admin".
            // Usamos SQL crudo para tener IF NOT EXISTS y evitar errores
            // si el usuario ya fue creado por otro mecanismo (ej. Program.cs).
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = 'admin')
                BEGIN
                    INSERT INTO Users (Username, PasswordHash, Role)
                    VALUES ('admin', '$2b$12$/G2bt9c5ZpWWv5bQQvyfwOwIUZt/8lp4Osfg8HCvuG9IaziGg.oMu', 'Admin')
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM Users WHERE Username = 'admin'");
        }
    }
}
