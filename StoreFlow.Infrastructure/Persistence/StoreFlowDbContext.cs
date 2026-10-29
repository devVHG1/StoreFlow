using Microsoft.EntityFrameworkCore;
using StoreFlow.Domain.Entities;
using StoreFlow.Application.Interfaces;

namespace StoreFlow.Infrastructure.Persistence;

public class StoreFlowDbContext : DbContext
{
    public StoreFlowDbContext(DbContextOptions<StoreFlowDbContext> options, ICurrentUser? current = null)
        : base(options)
    {
        this.current = current;
    }

    private readonly ICurrentUser? current;
    public string AuditReason { get; set; } = "CatalogAdjustment";
    public Guid? AuditOrderReference { get; set; }
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<OrderStatusEvent> OrderStatusEvents => Set<OrderStatusEvent>();
    public DbSet<OrderRequest> OrderRequests => Set<OrderRequest>();

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var owned = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(ct) : null;
        try
        {
            var actor = current?.AuditId is int actorId ? actorId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var reference = AuditOrderReference?.ToString() ?? "";
            await Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('storeflow.actor', {actor}, true), set_config('storeflow.reason', {AuditReason}, true), set_config('storeflow.order', {reference}, true)", ct);
            var count = await base.SaveChangesAsync(ct);
            if (owned is not null) await owned.CommitAsync(ct);
            return count;
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Product>().Property(x => x.Version).IsRowVersion().HasColumnName("xmin");
        modelBuilder.Entity<Order>().HasIndex(x => new { x.Status, x.ReservationExpiresAtUtc });
        modelBuilder.Entity<OrderRequest>().Property(x => x.Key).HasMaxLength(128);
        modelBuilder.Entity<OrderRequest>().HasIndex(x => new { x.ActorId, x.Key }).IsUnique();
        modelBuilder.Entity<OrderRequest>().HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StockMovement>().HasIndex(x => new { x.ProductId, x.Id });
        modelBuilder.Entity<OrderStatusEvent>().HasIndex(x => new { x.OrderId, x.Id });
        modelBuilder.Entity<User>().HasIndex(x => x.NormalizedEmail).IsUnique();
        modelBuilder.Entity<User>().Property(x => x.NormalizedEmail).HasMaxLength(254);
        modelBuilder.Entity<Product>()
            .HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItem>()
            .HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
