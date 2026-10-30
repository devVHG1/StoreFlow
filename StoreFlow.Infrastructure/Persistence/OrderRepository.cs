using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Application.DTOs;
using StoreFlow.Domain.Enums;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Infrastructure.Persistence;

public class OrderRepository(StoreFlowDbContext db) : IOrderRepository
{
    public async Task<(List<Order> Items, int TotalCount)> GetAllAsync(OrderQuery query, int? userId, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking().Where(x => !x.IsDeleted && (userId == null || x.UserId == userId));
        if (!string.IsNullOrEmpty(query.Status))
        {
            var status = Enum.Parse<OrderStatus>(query.Status);
            orders = orders.Where(x => x.Status == status);
        }
        // Кількість і позиції належать одному знімку даних.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var total = await orders.CountAsync(ct);
        var items = await orders.OrderByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).Include(x => x.Items).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return (items, total);
    }

    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

    public async Task<Order?> GetForUpdateAsync(int id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await db.Orders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public Task<bool> UserExistsAsync(int id, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.Id == id, ct);

    // Однаковий порядок блокувань зменшує ризик взаємного очікування.
    public Task<List<Product>> LockProductsAsync(int[] ids, CancellationToken ct) =>
        db.Products.FromSqlInterpolated(
            $"SELECT *, xmin FROM \"Products\" WHERE \"Id\" = ANY ({ids}) ORDER BY \"Id\" FOR UPDATE").ToListAsync(ct);

    public void SetAudit(string reason, Guid? reference) { db.AuditReason = reason; db.AuditOrderReference = reference; }
    public async Task<OrderRequest?> FindRequestAsync(int actorId, string key, CancellationToken ct)
    {
        var scope = actorId + ":" + key;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))", ct);
        return await db.OrderRequests.Include(x => x.Order).ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.ActorId == actorId && x.Key == key, ct);
    }
    public void AddRequest(OrderRequest request) => db.OrderRequests.Add(request);
    public Task<List<int>> FindExpiredAsync(DateTime now, CancellationToken ct) => db.Orders.AsNoTracking()
        .Where(x => !x.IsDeleted && x.Status == StoreFlow.Domain.Enums.OrderStatus.Pending && x.ReservationExpiresAtUtc <= now)
        .OrderBy(x => x.ReservationExpiresAtUtc).Take(100).Select(x => x.Id).ToListAsync(ct);
    public void Add(Order order) => db.Orders.Add(order);
    public void Remove(Order order) => order.IsDeleted = true;
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
