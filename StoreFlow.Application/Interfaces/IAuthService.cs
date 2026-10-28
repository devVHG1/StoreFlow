using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface IAuthService
{
    Task<AuthDto> RegisterAsync(RegisterDto request, CancellationToken ct);
    Task<AuthDto> LoginAsync(LoginDto request, CancellationToken ct);
    Task LogoutAsync(CancellationToken ct);
    Task<UserDto> BootstrapAdminAsync(RegisterDto request, CancellationToken ct);
}
