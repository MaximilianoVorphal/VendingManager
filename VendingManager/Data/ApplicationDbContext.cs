using Microsoft.EntityFrameworkCore;
using VendingManager.Models;

namespace VendingManager.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Maquina> Maquinas { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<ConfiguracionSlot> ConfiguracionSlots { get; set; }
        public DbSet<Venta> Ventas { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuramos precios como decimales (importante para dinero)
            modelBuilder.Entity<Venta>()
                .Property(v => v.PrecioVenta).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ConfiguracionSlot>()
                .Property(c => c.PrecioVenta).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Producto>()
                .Property(p => p.CostoPromedio).HasColumnType("decimal(18,2)");
        }
    }
}