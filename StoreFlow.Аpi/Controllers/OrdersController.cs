using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;

namespace StoreFlow.Аpi.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<OrderDto>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderDto request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/items")]
    public async Task<ActionResult<OrderDto>> AddItem(int id, CreateOrderItemDto request, CancellationToken ct) =>
        Ok(await service.AddItemAsync(id, request, ct));

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<ActionResult<OrderDto>> UpdateItem(int id, int itemId, UpdateOrderItemRequest request, CancellationToken ct) =>
        Ok(await service.UpdateItemAsync(id, itemId, request, ct));

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<ActionResult<OrderDto>> RemoveItem(int id, int itemId, CancellationToken ct) =>
        Ok(await service.RemoveItemAsync(id, itemId, ct));

    [HttpPatch("{id:int}/status")]
    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<OrderDto>> ChangeStatus(int id, UpdateOrderStatusRequest request, CancellationToken ct) =>
        Ok(await service.ChangeStatusAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

