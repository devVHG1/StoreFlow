using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Infrastructure.Persistence;

public class OrderRepository(StoreFlowDbContext db) : IOrderRepository
{
    public Task<List<Order>> GetAllAsync(int? userId, CancellationToken ct) =>
        db.Orders.AsNoTracking().Where(x => userId == null || x.UserId == userId)
            .Include(x => x.Items).OrderBy(x => x.Id).ToListAsync(ct);

    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Order?> GetForUpdateAsync(int id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await db.Orders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<bool> UserExistsAsync(int id, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.Id == id, ct);

    // Однаковий порядок блокувань зменшує ризик взаємного очікування.
    public Task<List<Product>> LockProductsAsync(int[] ids, CancellationToken ct) =>
        db.Products.FromSqlInterpolated(
            $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR UPDATE").ToListAsync(ct);

    public void Add(Order order) => db.Orders.Add(order);
    public void Remove(Order order) => db.Orders.Remove(order);
    public void RemoveItem(OrderItem item) => db.OrderItems.Remove(item);

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
