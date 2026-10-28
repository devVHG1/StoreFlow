using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Security;

namespace StoreFlow.Аpi.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController(IUserService service) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken ct) => Ok(await service.GetMeAsync(ct));

    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileDto request, CancellationToken ct) =>
        Ok(await service.UpdateProfileAsync(request, ct));

    [HttpPut("me/password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto request, CancellationToken ct)
    {
        await service.ChangePasswordAsync(request, ct);
        return NoContent();
    }

    [Authorize(Roles = Roles.Admin), HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [Authorize(Roles = Roles.Admin), HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));

    [Authorize(Roles = Roles.Admin), HttpPut("{id:int}/role")]
    public async Task<ActionResult<UserDto>> ChangeRole(int id, ChangeRoleDto request, CancellationToken ct) =>
        Ok(await service.ChangeRoleAsync(id, request, ct));
}
