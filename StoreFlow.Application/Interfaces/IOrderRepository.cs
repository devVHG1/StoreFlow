using StoreFlow.Domain.Entities;

namespace StoreFlow.Application.Interfaces;

public interface IOrderRepository
{
    Task<List<Order>> GetAllAsync(int? userId, CancellationToken ct);
    Task<Order?> GetAsync(int id, CancellationToken ct);
    Task<Order?> GetForUpdateAsync(int id, CancellationToken ct);
    Task<bool> UserExistsAsync(int id, CancellationToken ct);
    Task<List<Product>> LockProductsAsync(int[] ids, CancellationToken ct);
    void Add(Order order);
    void Remove(Order order);
    void RemoveItem(OrderItem item);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
}
