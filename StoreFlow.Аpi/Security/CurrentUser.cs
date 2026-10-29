using System.Security.Claims;
using StoreFlow.Application.Interfaces;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Security;

namespace StoreFlow.Аpi.Security;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    public int Id => int.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id :
        throw new BusinessException(401, "Потрібно увійти.");
    public int? AuditId => int.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id : null;
    public bool IsStaff => IsAdmin || Principal?.IsInRole(Roles.Manager) == true;
    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) == true;
}
