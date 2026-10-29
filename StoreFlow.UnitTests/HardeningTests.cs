using StoreFlow.Application.DTOs;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enums;
using Xunit;

namespace StoreFlow.UnitTests;

public class HardeningTests
{
    [Fact]
    public async Task RepeatedRequestDoesNotReserveAgain()
    {
        var repo = new Orders();
        repo.Products.Add(new() { Id = 1, Price = 10, StockQuantity = 5 });
        var service = new OrderService(repo, new Current());
        var request = new CreateOrderDto { IdempotencyKey = "same-request", Items = [new() { ProductId = 1, Quantity = 2 }] };
        var first = await service.CreateAsync(request, TestContext.Current.CancellationToken);
        var repeated = await service.CreateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Single(repo.Rows);
        Assert.Single(repo.Requests);
        Assert.Equal(3, repo.Products[0].StockQuantity);
    }

    [Fact]
    public async Task ChangedPayloadCannotReuseKey()
    {
        var repo = new Orders();
        repo.Products.Add(new() { Id = 1, Price = 10, StockQuantity = 5 });
        var service = new OrderService(repo, new Current());
        await service.CreateAsync(new() { IdempotencyKey = "key", Items = [new() { ProductId = 1, Quantity = 1 }] }, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(new() { IdempotencyKey = "key", Items = [new() { ProductId = 1, Quantity = 2 }] }, TestContext.Current.CancellationToken));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(4, repo.Products[0].StockQuantity);
    }

    [Theory]
    [InlineData(null, 400)]
    [InlineData(6u, 409)]
    public async Task MissingOrStaleProductVersionCannotChangeStock(uint? version, int status)
    {
        var repo = new Catalog();
        repo.Categories.Add(new() { Id = 1 });
        repo.Products.Add(new() { Id = 1, Version = 7, StockQuantity = 8, Price = 10, CategoryId = 1 });
        var error = await Assert.ThrowsAnyAsync<BusinessException>(() => new ProductService(repo).UpdateAsync(1,
            new() { Name = "Item", CategoryId = 1, Price = 10, StockQuantity = 10, Version = version }, TestContext.Current.CancellationToken));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(8, repo.Products[0].StockQuantity);
    }

    [Fact]
    public async Task ManualStockChangeRequiresReason()
    {
        var repo = new Catalog();
        repo.Categories.Add(new() { Id = 1 });
        repo.Products.Add(new() { Id = 1, Version = 7, StockQuantity = 8 });
        var service = new ProductService(repo);
        var request = new ProductRequest { Name = "Item", CategoryId = 1, Price = 10, StockQuantity = 9, Version = 7 };
        await Assert.ThrowsAsync<RequestValidationException>(() => service.UpdateAsync(1, request, TestContext.Current.CancellationToken));
        Assert.Equal(8, repo.Products[0].StockQuantity);
        request.StockChangeReason = "Надходження товару";
        await service.UpdateAsync(1, request, TestContext.Current.CancellationToken);
        Assert.Equal(9, repo.Products[0].StockQuantity);
    }

    [Fact]
    public async Task ExpirationRestoresStockOnceAndCannotReviveOrder()
    {
        var repo = new Orders();
        repo.Products.Add(new() { Id = 1, Price = 10, StockQuantity = 8 });
        repo.Rows.Add(new() { Id = 1, UserId = 1, ReservationExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), Items = [new() { ProductId = 1, Quantity = 2, UnitPrice = 10 }] });
        var service = new OrderService(repo, new Current { Manager = true });
        await Assert.ThrowsAsync<BusinessException>(() => service.AddItemAsync(1, new() { ProductId = 1, Quantity = 1 }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<BusinessException>(() => service.ChangeStatusAsync(1, new() { Status = OrderStatus.Processing }, TestContext.Current.CancellationToken));
        await service.ExpireReservationsAsync(TestContext.Current.CancellationToken);
        await service.ExpireReservationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, repo.Products[0].StockQuantity);
        Assert.Equal(OrderStatus.Cancelled, repo.Rows[0].Status);
        Assert.Null(repo.Rows[0].ReservationExpiresAtUtc);
    }
}
