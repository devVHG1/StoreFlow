using Microsoft.EntityFrameworkCore;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Infrastructure.Persistence;

public class StoreFlowDbContext : DbContext
{
    public StoreFlowDbContext(DbContextOptions<StoreFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
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
