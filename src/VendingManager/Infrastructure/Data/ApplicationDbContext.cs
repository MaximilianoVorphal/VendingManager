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
        public DbSet<SnapshotSlot> SnapshotSlots { get; set; } = null!;
        public DbSet<Auditoria> Auditoria { get; set; } = null!;
        public DbSet<Compra> Compras { get; set; } = null!;
        public DbSet<DetalleCompra> DetallesCompra { get; set; } = null!;
        public DbSet<GastoRecurrente> GastosRecurrentes { get; set; } = null!;
        public DbSet<CompraHistory> ComprasHistory { get; set; } = null!;
        public DbSet<ProductoHistory> ProductosHistory { get; set; } = null!;
        public DbSet<MaquinaHistory> MaquinasHistory { get; set; } = null!;
        public DbSet<VentaHistory> VentasHistory { get; set; } = null!;
        public DbSet<MovimientoCajaHistory> MovimientosCajaHistory { get; set; } = null!;
        public DbSet<ConfiguracionSlotHistory> ConfiguracionSlotsHistory { get; set; } = null!;
        public DbSet<GastoRecurrenteHistory> GastosRecurrentesHistory { get; set; } = null!;
        public DbSet<OrdenCargaHistory> OrdenesCargaHistory { get; set; } = null!;
        public DbSet<UserHistory> UsersHistory { get; set; } = null!;
        public DbSet<ProductoCosto> ProductoCostos { get; set; } = null!;

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

            modelBuilder.Entity<DetalleOrdenCarga>()
                .Property(d => d.CostoUnitario).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ProductoCosto>(e =>
            {
                e.ToTable("ProductoCostos");
                e.HasKey(p => p.Id);
                e.Property(p => p.Costo).HasColumnType("decimal(18,2)");
                e.Property(p => p.FechaDesde).HasColumnType("datetime2");
                e.Property(p => p.FechaHasta).HasColumnType("datetime2");

                e.HasIndex(p => new { p.ProductoId, p.FechaDesde })
                    .IncludeProperties(p => p.FechaHasta);

                e.HasOne(p => p.Producto)
                    .WithMany()
                    .HasForeignKey(p => p.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
