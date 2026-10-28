using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Security;

namespace StoreFlow.Application.Services;

public class AuthService(IUserRepository users, IPasswordService passwords, ITokenService tokens, ICurrentUser current) : IAuthService
{
    public async Task<AuthDto> RegisterAsync(RegisterDto request, CancellationToken ct)
    {
        var user = await users.InTransactionAsync(async () =>
        {
            if (await users.GetByEmailForUpdateAsync(Normalize(request.Email), ct) is not null)
                throw RequestValidationException.DuplicateEmail();
            var created = NewUser(request, Roles.Customer);
            users.Add(created);
            return created;
        }, ct);
        return tokens.Create(user);
    }

    public async Task<AuthDto> LoginAsync(LoginDto request, CancellationToken ct)
    {
        var user = await users.InTransactionAsync(async () =>
        {
            var found = await users.GetByEmailForUpdateAsync(Normalize(request.Email), ct);
            var check = passwords.Verify(found?.PasswordHash, request.Password);
            if (found is null || found.LockoutEndUtc > DateTime.UtcNow) return null;
            if (!check.IsValid)
            {
                found.FailedLoginAttempts++;
                if (found.FailedLoginAttempts >= 5)
                {
                    found.LockoutEndUtc = DateTime.UtcNow.AddMinutes(15);
                    found.FailedLoginAttempts = 0;
                }
                return null;
            }
            found.FailedLoginAttempts = 0;
            found.LockoutEndUtc = null;
            if (check.NeedsRehash) found.PasswordHash = passwords.Hash(request.Password);
            return found;
        }, ct);
        if (user is null || !Roles.IsValid(user.Role)) throw new BusinessException(401, "Невірна адреса або пароль, або вхід тимчасово обмежено.");
        return tokens.Create(user);
    }

    public async Task LogoutAsync(CancellationToken ct) =>
        await users.InTransactionAsync(async () =>
        {
            var user = await users.GetForUpdateAsync(current.Id, ct) ?? throw new BusinessException(401, "Потрібно увійти повторно.");
            InvalidateTokens(user);
            return true;
        }, ct);

    public async Task<UserDto> BootstrapAdminAsync(RegisterDto request, CancellationToken ct)
    {
        var user = await users.InTransactionAsync(async () =>
        {
            if (await users.CountAdminsAsync(ct) > 0)
                throw new BusinessException(409, "Адміністратор уже існує.");
            if (await users.GetByEmailForUpdateAsync(Normalize(request.Email), ct) is not null)
                throw RequestValidationException.DuplicateEmail();
            var created = NewUser(request, Roles.Admin);
            users.Add(created);
            return created;
        }, ct, lockRoles: true);
        return Map(user);
    }

    private User NewUser(RegisterDto request, string role) => new()
    {
        FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
        Email = request.Email.Trim(), NormalizedEmail = Normalize(request.Email),
        PasswordHash = passwords.Hash(request.Password), Role = role
    };

    internal static string Normalize(string email) => email.Trim().ToLowerInvariant();
    internal static UserDto Map(User user) => new(user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.CreatedAt);
    internal static void InvalidateTokens(User user)
    {
        if (user.TokenVersion == int.MaxValue) throw new BusinessException(409, "Не вдалося оновити облікові дані.");
        user.TokenVersion++;
    }
}
