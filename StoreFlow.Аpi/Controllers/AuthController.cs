using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;

namespace StoreFlow.Аpi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService service) : ControllerBase
{
    [AllowAnonymous, EnableRateLimiting("auth"), HttpPost("register")]
    public async Task<ActionResult<AuthDto>> Register(RegisterDto request, CancellationToken ct)
    {
        var result = await service.RegisterAsync(request, ct);
        return Created("/api/users/me", result);
    }

    [AllowAnonymous, EnableRateLimiting("auth"), HttpPost("login")]
    public async Task<ActionResult<AuthDto>> Login(LoginDto request, CancellationToken ct) =>
        Ok(await service.LoginAsync(request, ct));

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await service.LogoutAsync(ct);
        return NoContent();
    }
}
