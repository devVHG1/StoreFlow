using System.Security.Cryptography;
using System.Text;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enums;

namespace StoreFlow.Application.Services;

public class OrderService(IOrderRepository repository, ICurrentUser current) : IOrderService
{
    public async Task<PagedResult<OrderDto>> GetAllAsync(OrderQuery query, CancellationToken ct)
    {
        if (!current.IsStaff && query.UserId is int requested && requested != current.Id)
            throw new BusinessException(403, "Не можна переглядати замовлення іншого користувача.");
        var result = await repository.GetAllAsync(query, current.IsStaff ? query.UserId : current.Id, ct);
        return new(result.Items.Select(Map).ToList(), query.Page, query.PageSize, result.TotalCount);
    }

    public async Task<OrderDto> GetAsync(int id, CancellationToken ct)
    {
        var order = await repository.GetAsync(id, ct) ?? throw Missing();
        EnsureAccess(order);
        return Map(order);
    }

    public Task<OrderDto> CreateAsync(CreateOrderDto request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var userId = request.UserId ?? current.Id;
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                userId + ":" + string.Join(";", request.Items.OrderBy(x => x.ProductId).Select(x => $"{x.ProductId}:{x.Quantity}")))));
            if (request.IdempotencyKey is { } key)
            {
                var previous = await repository.FindRequestAsync(current.Id, key, ct);
                if (previous is not null)
                {
                    if (previous.Fingerprint != fingerprint || previous.Order.IsDeleted)
                        throw new BusinessException(409, "Ключ уже використано для іншого або видаленого замовлення.");
                    EnsureAccess(previous.Order);
                    return previous.Order;
                }
            }
            if (!current.IsStaff && userId != current.Id)
                throw new BusinessException(403, "Не можна створювати замовлення іншого користувача.");
            if (!await repository.UserExistsAsync(userId, ct))
                throw new BusinessException(400, "Користувача не знайдено.");
            if (request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count)
                throw new BusinessException(400, "Товар не може повторюватися в запиті.");
            var products = await repository.LockProductsAsync(request.Items.Select(x => x.ProductId).ToArray(), ct);
            var order = new Order { UserId = userId, ReservationExpiresAtUtc = DateTime.UtcNow.AddMinutes(30) };
            repository.SetAudit("OrderCreated", order.Reference);
            foreach (var line in request.Items)
            {
                var product = products.SingleOrDefault(x => x.Id == line.ProductId)
                    ?? throw new BusinessException(404, "Товар не знайдено.");
                Reserve(product, line.Quantity);
                order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, Quantity = line.Quantity, UnitPrice = product.Price });
            }
            Recalculate(order);
            repository.Add(order);
            if (request.IdempotencyKey is { } requestKey)
                repository.AddRequest(new OrderRequest { ActorId = current.Id, Key = requestKey, Fingerprint = fingerprint, Order = order });
            return order;
        }, ct).ToDto();

    public Task<OrderDto> AddItemAsync(int id, CreateOrderItemDto request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var order = await FindLockedAsync(id, ct);
            EnsurePending(order);
            repository.SetAudit("OrderItemsChanged", order.Reference);
            var product = (await repository.LockProductsAsync([request.ProductId], ct)).SingleOrDefault()
                ?? throw new BusinessException(404, "Товар не знайдено.");
            var item = order.Items.SingleOrDefault(x => x.ProductId == request.ProductId);
            if (item is null && order.Items.Count >= 100)
                throw new BusinessException(400, "Замовлення може містити до 100 різних товарів.");
            Reserve(product, request.Quantity);
            if (item is null)
                order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, Quantity = request.Quantity, UnitPrice = product.Price });
            else
                item.Quantity = AddQuantity(item.Quantity, request.Quantity);
            Recalculate(order);
            return order;
        }, ct).ToDto();

    public Task<OrderDto> UpdateItemAsync(int id, int itemId, UpdateOrderItemRequest request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var order = await FindLockedAsync(id, ct);
            EnsurePending(order);
            repository.SetAudit("OrderItemsChanged", order.Reference);
            var item = FindItem(order, itemId);
            var product = (await repository.LockProductsAsync([item.ProductId], ct)).Single();
            var difference = request.Quantity - item.Quantity;
            if (difference > 0) Reserve(product, difference);
            else product.StockQuantity = AddQuantity(product.StockQuantity, -difference);
            item.Quantity = request.Quantity;
            Recalculate(order);
            return order;
        }, ct).ToDto();

    public Task<OrderDto> RemoveItemAsync(int id, int itemId, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var order = await FindLockedAsync(id, ct);
            EnsurePending(order);
            repository.SetAudit("OrderItemsChanged", order.Reference);
            var item = FindItem(order, itemId);
            if (order.Items.Count == 1)
                throw new BusinessException(409, "Не можна видалити останню позицію. Скасуйте замовлення.");
            var product = (await repository.LockProductsAsync([item.ProductId], ct)).Single();
            product.StockQuantity = AddQuantity(product.StockQuantity, item.Quantity);
            order.Items.Remove(item);
            repository.RemoveItem(item);
            Recalculate(order);
            return order;
        }, ct).ToDto();

    public Task<OrderDto> ChangeStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var order = await FindLockedAsync(id, ct);
            if (!current.IsStaff && request.Status != OrderStatus.Cancelled)
                throw new BusinessException(403, "Клієнт може лише скасувати власне замовлення.");
            if (order.Status == request.Status) return order;
            if (order.Status == OrderStatus.Pending && order.ReservationExpiresAtUtc <= DateTime.UtcNow && request.Status != OrderStatus.Cancelled)
                throw new BusinessException(409, "Строк резервування завершився.");
            repository.SetAudit(string.IsNullOrWhiteSpace(request.Reason) ? "StatusChanged" : "StatusChanged: " + request.Reason.Trim(), order.Reference);
            var allowed = (order.Status, request.Status) switch
            {
                (OrderStatus.Pending, OrderStatus.Processing or OrderStatus.Cancelled) => true,
                (OrderStatus.Processing, OrderStatus.Shipped or OrderStatus.Cancelled) => true,
                (OrderStatus.Shipped, OrderStatus.Completed) => true,
                _ => false
            };
            if (!allowed) throw new BusinessException(409, "Такий перехід статусу недоступний.");
            if (request.Status == OrderStatus.Cancelled) await RestoreAsync(order, ct);
            order.Status = request.Status;
            order.ReservationExpiresAtUtc = null;
            return order;
        }, ct).ToDto();

    public async Task DeleteAsync(int id, CancellationToken ct) =>
        await repository.InTransactionAsync(async () =>
        {
            var order = await FindLockedAsync(id, ct);
            if (order.Status is not (OrderStatus.Pending or OrderStatus.Cancelled))
                throw new BusinessException(409, "Видалити можна лише нове або скасоване замовлення.");
            repository.SetAudit("OrderDeleted", order.Reference);
            if (order.Status == OrderStatus.Pending) await RestoreAsync(order, ct);
            order.ReservationExpiresAtUtc = null;
            repository.Remove(order);
            return true;
        }, ct);

    public async Task ExpireReservationsAsync(CancellationToken ct)
    {
        foreach (var id in await repository.FindExpiredAsync(DateTime.UtcNow, ct))
            await repository.InTransactionAsync(async () =>
            {
                var order = await repository.GetForUpdateAsync(id, ct);
                if (order is null || order.Status != OrderStatus.Pending || order.ReservationExpiresAtUtc is null || order.ReservationExpiresAtUtc > DateTime.UtcNow) return false;
                repository.SetAudit("ReservationExpired", order.Reference);
                await RestoreAsync(order, ct);
                order.Status = OrderStatus.Cancelled;
                order.ReservationExpiresAtUtc = null;
                return true;
            }, ct);
    }

    private async Task<Order> FindLockedAsync(int id, CancellationToken ct)
    {
        var order = await repository.GetForUpdateAsync(id, ct) ?? throw Missing();
        EnsureAccess(order);
        return order;
    }

    private void EnsureAccess(Order order)
    {
        if (!current.IsStaff && order.UserId != current.Id) throw Missing();
    }

    private async Task RestoreAsync(Order order, CancellationToken ct)
    {
        var products = await repository.LockProductsAsync(order.Items.Select(x => x.ProductId).ToArray(), ct);
        foreach (var item in order.Items)
        {
            var product = products.Single(x => x.Id == item.ProductId);
            product.StockQuantity = AddQuantity(product.StockQuantity, item.Quantity);
        }
    }

    private static void EnsurePending(Order order)
    {
        if (order.ReservationExpiresAtUtc <= DateTime.UtcNow)
            throw new BusinessException(409, "Строк резервування завершився.");
        if (order.Status != OrderStatus.Pending)
            throw new BusinessException(409, "Позиції можна змінювати лише в новому замовленні.");
    }

    private static void Reserve(Product product, int quantity)
    {
        if (product.StockQuantity < quantity)
            throw new BusinessException(400, $"Недостатньо товару з id {product.Id} на складі.");
        product.StockQuantity -= quantity;
    }

    private static int AddQuantity(int value, int amount)
    {
        try { return checked(value + amount); }
        catch (OverflowException) { throw new BusinessException(409, "Кількість перевищує допустиме значення."); }
    }

    private static void Recalculate(Order order)
    {
        try { order.TotalAmount = order.Items.Sum(x => checked(x.UnitPrice * x.Quantity)); }
        catch (OverflowException) { throw new BusinessException(409, "Сума замовлення перевищує допустиме значення."); }
    }

    private static BusinessException Missing() => new(404, "Замовлення не знайдено.");
    private static OrderItem FindItem(Order order, int itemId) =>
        order.Items.SingleOrDefault(x => x.Id == itemId) ?? throw new BusinessException(404, "Позицію не знайдено.");

    internal static OrderDto Map(Order order) => new(order.Id, order.UserId, order.CreatedAt, order.Status,
        order.TotalAmount, order.Items.OrderBy(x => x.Id).Select(x =>
            new OrderItemDto(x.Id, x.ProductId, x.Quantity, x.UnitPrice, x.Quantity * x.UnitPrice, x.ProductName, x.IsNameBackfilled)).ToList(), order.ReservationExpiresAtUtc);
}

internal static class OrderTaskExtensions
{
    public static async Task<OrderDto> ToDto(this Task<Order> task) => OrderService.Map(await task);
}

