using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Security;

namespace StoreFlow.Application.Services;

public class UserService(IUserRepository users, IPasswordService passwords, ICurrentUser current) : IUserService
{
    public async Task<List<UserDto>> GetAllAsync(CancellationToken ct)
    {
        EnsureAdmin();
        return (await users.GetAllAsync(ct)).Select(AuthService.Map).ToList();
    }

    public async Task<UserDto> GetAsync(int id, CancellationToken ct)
    {
        EnsureAdmin();
        return AuthService.Map(await users.GetAsync(id, ct) ?? throw new BusinessException(404, "Користувача не знайдено."));
    }

    public async Task<UserDto> GetMeAsync(CancellationToken ct) =>
        AuthService.Map(await users.GetAsync(current.Id, ct) ?? throw new BusinessException(401, "Потрібно увійти повторно."));

    public async Task<UserDto> UpdateProfileAsync(UpdateProfileDto request, CancellationToken ct)
    {
        var user = await users.InTransactionAsync(async () =>
        {
            var found = await users.GetForUpdateAsync(current.Id, ct) ?? throw new BusinessException(401, "Потрібно увійти повторно.");
            found.FirstName = request.FirstName.Trim();
            found.LastName = request.LastName.Trim();
            return found;
        }, ct);
        return AuthService.Map(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordDto request, CancellationToken ct) =>
        await users.InTransactionAsync(async () =>
        {
            var user = await users.GetForUpdateAsync(current.Id, ct) ?? throw new BusinessException(401, "Потрібно увійти повторно.");
            if (!passwords.Verify(user.PasswordHash, request.CurrentPassword).IsValid)
                throw new BusinessException(400, "Поточний пароль неправильний.");
            user.PasswordHash = passwords.Hash(request.NewPassword);
            user.FailedLoginAttempts = 0;
            user.LockoutEndUtc = null;
            AuthService.InvalidateTokens(user);
            return true;
        }, ct);

    public async Task<UserDto> ChangeRoleAsync(int id, ChangeRoleDto request, CancellationToken ct)
    {
        EnsureAdmin();
        if (!Roles.IsValid(request.Role)) throw new BusinessException(400, "Невідома роль.");
        var user = await users.InTransactionAsync(async () =>
        {
            var found = await users.GetForUpdateAsync(id, ct) ?? throw new BusinessException(404, "Користувача не знайдено.");
            if (found.Role == request.Role) return found;
            if (found.Role == Roles.Admin && request.Role != Roles.Admin && await users.CountAdminsAsync(ct) == 1)
                throw new BusinessException(409, "Не можна змінити роль останнього адміністратора.");
            found.Role = request.Role;
            AuthService.InvalidateTokens(found);
            return found;
        }, ct, lockRoles: true);
        return AuthService.Map(user);
    }

    private void EnsureAdmin()
    {
        if (!current.IsAdmin) throw new BusinessException(403, "Недостатньо прав.");
    }
}
