using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Security;

namespace StoreFlow.Infrastructure.Persistence;

public class UserRepository(StoreFlowDbContext db) : IUserRepository
{
    public Task<List<User>> GetAllAsync(CancellationToken ct) =>
        db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    public Task<User?> GetAsync(int id, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<User?> GetForUpdateAsync(int id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Users\" WHERE \"Id\"={id} FOR UPDATE", ct);
        return await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
    }
    public Task<User?> GetByEmailForUpdateAsync(string email, CancellationToken ct) =>
        db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"NormalizedEmail\"={email} FOR UPDATE").SingleOrDefaultAsync(ct);
    public Task<int> CountAdminsAsync(CancellationToken ct) => db.Users.CountAsync(x => x.Role == Roles.Admin, ct);
    public void Add(User user) => db.Users.Add(user);
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct, bool lockRoles = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (lockRoles)
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73419021)", ct);
        var result = await action();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
