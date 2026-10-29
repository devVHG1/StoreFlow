using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Security;
using StoreFlow.Infrastructure.Persistence;
namespace StoreFlow.Аpi.Controllers;

[ApiController, Authorize, Route("api")]
public class AuditController(StoreFlowDbContext db, IOrderService orders, ICurrentUser current) : ControllerBase
{
    [HttpGet("orders/{id:int}/history")]
    public async Task<ActionResult<List<OrderStatusEvent>>> History(int id, CancellationToken ct)
    {
        if (current.IsStaff)
        {
            if (!await db.Orders.AnyAsync(x => x.Id == id, ct)) throw new BusinessException(404, "Замовлення не знайдено.");
        }
        else await orders.GetAsync(id, ct);
        return await db.OrderStatusEvents.AsNoTracking().Where(x => x.OrderId == id).OrderBy(x => x.Id).ToListAsync(ct);
    }
    [HttpGet("stock-movements"), Authorize(Roles = Roles.Staff)]
    public async Task<ActionResult<List<StockMovement>>> Stock([FromQuery] int? productId, [FromQuery] long afterId = 0, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (afterId < 0 || limit is < 1 or > 100 || productId is <= 0) throw new BusinessException(400, "Некоректні параметри журналу.");
        return await db.StockMovements.AsNoTracking().Where(x => x.Id > afterId && (productId == null || x.ProductId == productId))
            .OrderBy(x => x.Id).Take(limit).ToListAsync(ct);
    }
}
