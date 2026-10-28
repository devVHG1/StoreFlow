using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetAllAsync(CancellationToken ct);
    Task<UserDto> GetAsync(int id, CancellationToken ct);
    Task<UserDto> GetMeAsync(CancellationToken ct);
    Task<UserDto> UpdateProfileAsync(UpdateProfileDto request, CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordDto request, CancellationToken ct);
    Task<UserDto> ChangeRoleAsync(int id, ChangeRoleDto request, CancellationToken ct);
}
