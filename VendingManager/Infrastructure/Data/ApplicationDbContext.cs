using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Maquina> Maquinas { get; set; } = null!;
        public DbSet<Producto> Productos { get; set; } = null!;
        public DbSet<ConfiguracionSlot> ConfiguracionSlots { get; set; } = null!;
        public DbSet<Venta> Ventas { get; set; } = null!;
        public DbSet<MovimientoCaja> MovimientosCaja { get; set; } = null!;
        public DbSet<Informe> Informes { get; set; } = null!;
        public DbSet<OrdenCarga> OrdenesCarga { get; set; } = null!;
        public DbSet<DetalleOrdenCarga> DetallesOrdenCarga { get; set; } = null!;
        public DbSet<Core.Entities.User> Users { get; set; } = null!;
        public DbSet<TemplateRecarga> TemplatesRecarga { get; set; } = null!;
        public DbSet<PeriodoRecarga> PeriodosRecarga { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuramos precios como decimales (importante para dinero)
            modelBuilder.Entity<Venta>()
                .Property(v => v.PrecioVenta).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ConfiguracionSlot>()
                .Property(c => c.PrecioVenta).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Producto>()
                .Property(p => p.CostoPromedio).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Producto>()
                .Property(p => p.PrecioVenta).HasColumnType("decimal(18,2)");
        }
    }
}
