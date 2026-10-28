using StoreFlow.Domain.Entities;

namespace StoreFlow.Application.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync(CancellationToken ct);
    Task<User?> GetAsync(int id, CancellationToken ct);
    Task<User?> GetForUpdateAsync(int id, CancellationToken ct);
    Task<User?> GetByEmailForUpdateAsync(string email, CancellationToken ct);
    Task<int> CountAdminsAsync(CancellationToken ct);
    void Add(User user);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct, bool lockRoles = false);
}
