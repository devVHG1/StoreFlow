using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync(CancellationToken ct);
    Task<OrderDto> GetAsync(int id, CancellationToken ct);
    Task<OrderDto> CreateAsync(CreateOrderDto request, CancellationToken ct);
    Task<OrderDto> AddItemAsync(int id, CreateOrderItemDto request, CancellationToken ct);
    Task<OrderDto> UpdateItemAsync(int id, int itemId, UpdateOrderItemRequest request, CancellationToken ct);
    Task<OrderDto> RemoveItemAsync(int id, int itemId, CancellationToken ct);
    Task<OrderDto> ChangeStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken ct);
    Task ExpireReservationsAsync(CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}

