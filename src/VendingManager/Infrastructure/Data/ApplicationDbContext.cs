using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Shared.Enums;

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
        public DbSet<Transferencia> Transferencias { get; set; } = null!;
        public DbSet<Rendicion> Rendiciones { get; set; } = null!;
        public DbSet<AccountingPeriod> AccountingPeriods { get; set; } = null!;
        public DbSet<TransferenciaHistory> TransferenciasHistory { get; set; } = null!;
        public DbSet<RendicionHistory> RendicionesHistory { get; set; } = null!;
        public DbSet<ProductoEAN> ProductoEANs { get; set; } = null!;
        public DbSet<Devolucion> Devoluciones { get; set; } = null!;
        public DbSet<SyncMetadata> SyncMetadata { get; set; } = null!;
        public DbSet<ProveedorCatalog> ProveedorCatalog { get; set; } = null!;
        public DbSet<ProveedorAlias> ProveedorAlias { get; set; } = null!;
        public DbSet<ProveedorCatalogHistory> ProveedorCatalogHistory { get; set; } = null!;
        public DbSet<ZonaLogistica> ZonasLogisticas { get; set; } = null!;

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

            // TemplateRecarga: Estado enum as int + RowVersion concurrency token
            modelBuilder.Entity<TemplateRecarga>(e =>
            {
                e.Property(t => t.Estado)
                    .HasConversion<int>()
                    .HasDefaultValueSql("0"); // Pendiente (0) is default for new instances

                e.Property(t => t.RowVersion)
                    .IsRowVersion();
            });

            modelBuilder.Entity<ProductoCosto>(entity =>
            {
                entity.ToTable("ProductoCostos");
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Costo).HasColumnType("decimal(18,2)");
                entity.Property(p => p.FechaDesde).HasColumnType("datetime2");
                entity.Property(p => p.FechaHasta).HasColumnType("datetime2");

                entity.HasIndex(p => new { p.ProductoId, p.FechaDesde })
                    .IncludeProperties(p => p.FechaHasta);

                entity.HasOne(p => p.Producto)
                    .WithMany()
                    .HasForeignKey(p => p.ProductoId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Transferencia: enum as int + relationships
            modelBuilder.Entity<Transferencia>(e =>
            {
                e.Property(t => t.Estado).HasConversion<int>();
                e.Property(t => t.Monto).HasColumnType("decimal(18,2)");
                e.HasOne(t => t.Rendicion)
                    .WithMany(r => r.Transferencias)
                    .HasForeignKey(t => t.RendicionId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(t => t.MovimientoCaja)
                    .WithMany()
                    .HasForeignKey(t => t.MovimientoCajaId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(t => t.AccountingPeriod)
                    .WithMany(p => p.Transferencias)
                    .HasForeignKey(t => t.PeriodoId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasMany(t => t.Compras)
                    .WithOne(c => c.Transferencia)
                    .HasForeignKey(c => c.TransferenciaId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // AccountingPeriod: enum as int
            modelBuilder.Entity<AccountingPeriod>(e =>
            {
                e.Property(p => p.Estado).HasConversion<int>();
                e.HasMany(p => p.Transferencias)
                    .WithOne(t => t.AccountingPeriod)
                    .HasForeignKey(t => t.PeriodoId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Rendicion: enum as int + relationships
            modelBuilder.Entity<Rendicion>(e =>
            {
                e.Property(r => r.Estado).HasConversion<int>();
                e.HasMany(r => r.Transferencias)
                    .WithOne(t => t.Rendicion)
                    .HasForeignKey(t => t.RendicionId)
                    .OnDelete(DeleteBehavior.SetNull);
                e.HasMany(r => r.Gastos)
                    .WithOne(m => m.Rendicion)
                    .HasForeignKey(m => m.RendicionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Devolucion: nullable FKs with NoAction to avoid multiple-cascade-path errors on SQL Server
            modelBuilder.Entity<Devolucion>(e =>
            {
                e.Property(d => d.Monto).HasColumnType("decimal(18,2)");

                e.HasOne(d => d.Rendicion)
                    .WithMany()
                    .HasForeignKey(d => d.RendicionId)
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(d => d.AccountingPeriod)
                    .WithMany(p => p.Devoluciones)
                    .HasForeignKey(d => d.PeriodoId)
                    .OnDelete(DeleteBehavior.NoAction);

                e.HasOne(d => d.MovimientoCaja)
                    .WithMany()
                    .HasForeignKey(d => d.MovimientoCajaId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ProductoEAN: catálogo EAN/SKU con índice único y FK nullable a Producto
            modelBuilder.Entity<ProductoEAN>(entity =>
            {
                entity.ToTable("ProductoEAN");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.EAN)
                    .HasMaxLength(13);

                entity.Property(e => e.SKU)
                    .HasMaxLength(50);

                entity.Property(e => e.Proveedor)
                    .HasMaxLength(200);

                entity.Property(e => e.DescripcionProveedor)
                    .HasMaxLength(500);

                entity.Property(e => e.CreatedAt)
                    .HasColumnType("datetime2");

                entity.Property(e => e.LastSeenAt)
                    .HasColumnType("datetime2");

                // Índice único por EAN (filtrado: solo aplica unicidad cuando EAN no es null)
                entity.HasIndex(e => e.EAN)
                    .IsUnique()
                    .HasFilter("[EAN] IS NOT NULL")
                    .HasDatabaseName("IX_ProductoEAN_EAN");

                // FK nullable a Producto — si el producto se elimina, el mapeo
                // se desvincula pero no se pierde (la fila sigue existiendo)
                entity.HasOne(e => e.Producto)
                    .WithMany()
                    .HasForeignKey(e => e.ProductoId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ProveedorCatalog: canonical supplier identity
            // NOTE: unique index on NombreCanonical — SQL Server default collation is CI (case-insensitive)
            // so case uniqueness is enforced at DB level; verify this matches the target database collation
            // before going to production (design risk documented in apply-progress).
            modelBuilder.Entity<ProveedorCatalog>(entity =>
            {
                entity.ToTable("ProveedorCatalog");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.NombreCanonical)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.CreatedAt)
                    .HasColumnType("datetime2");

                entity.Property(e => e.LastSeenAt)
                    .HasColumnType("datetime2");

                entity.HasIndex(e => e.NombreCanonical)
                    .IsUnique()
                    .HasDatabaseName("IX_ProveedorCatalog_NombreCanonical");
            });

            // ProveedorAlias: raw OCR strings mapped to a canonical supplier
            modelBuilder.Entity<ProveedorAlias>(entity =>
            {
                entity.ToTable("ProveedorAlias");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RawName)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.RawNameNormalized)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.CreatedAt)
                    .HasColumnType("datetime2");

                entity.Property(e => e.LastSeenAt)
                    .HasColumnType("datetime2");

                // Unique filtered index on the normalized key — one alias belongs to exactly one catalog entry
                entity.HasIndex(e => e.RawNameNormalized)
                    .IsUnique()
                    .HasDatabaseName("IX_ProveedorAlias_RawNameNormalized");

                // Alias is meaningless without its canonical — cascade delete
                entity.HasOne(a => a.ProveedorCatalog)
                    .WithMany()
                    .HasForeignKey(a => a.ProveedorCatalogId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Compra -> ProveedorCatalog: nullable FK, SetNull on catalog deletion preserves the compra row
            modelBuilder.Entity<Compra>()
                .HasOne(c => c.ProveedorCatalog)
                .WithMany()
                .HasForeignKey(c => c.ProveedorCatalogId)
                .OnDelete(DeleteBehavior.SetNull);
<<<<<<< HEAD
=======

            // DepreciacionMaquina: decimal precision for money amounts
            modelBuilder.Entity<DepreciacionMaquina>(e =>
            {
                e.Property(d => d.ValorAdquisicion).HasColumnType("decimal(18,2)");
                e.Property(d => d.ValorResidual).HasColumnType("decimal(18,2)");
            });

            // MovimientoCaja -> Maquina: nullable FK, no navigation property
            modelBuilder.Entity<MovimientoCaja>()
                .HasOne<Maquina>()
                .WithMany()
                .HasForeignKey(m => m.MaquinaId)
                .OnDelete(DeleteBehavior.SetNull);

            // ZonaLogistica: costo base como decimal (dinero) + seed data
            modelBuilder.Entity<ZonaLogistica>()
                .Property(z => z.CostoBaseViaje).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<ZonaLogistica>().HasData(
                new ZonaLogistica { Id = 1, Nombre = "Zona Norte", CostoBaseViaje = 25000m },
                new ZonaLogistica { Id = 2, Nombre = "Zona Centro", CostoBaseViaje = 15000m },
                new ZonaLogistica { Id = 3, Nombre = "Zona Sur", CostoBaseViaje = 20000m }
            );

            // Maquina -> ZonaLogistica: FK nullable, si la zona se elimina la máquina queda sin zona
            modelBuilder.Entity<Maquina>()
                .HasOne(m => m.Zona)
                .WithMany()
                .HasForeignKey(m => m.ZonaLogisticaId)
                .OnDelete(DeleteBehavior.SetNull);
>>>>>>> d60068f (feat(logistica): predictive stockout and route optimization module by zone)
        }
    }
}
