using StoreFlow.Application.DTOs;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Enums;
using Xunit;

namespace StoreFlow.UnitTests;

public class OrderTests
{
    private static (Orders repo,OrderService service) Setup(OrderStatus status=OrderStatus.Pending,bool staff=true)
    {
        var repo=new Orders();repo.Products.Add(new(){Id=1,Name="Item",Price=10.25m,StockQuantity=8});
        repo.Rows.Add(new(){Id=1,UserId=1,Status=status,TotalAmount=20.5m,Items=[new(){Id=1,ProductId=1,Quantity=2,UnitPrice=10.25m}]});
        return(repo,new OrderService(repo,new Current{Manager=staff}));
    }
    [Fact]
    public async Task NewOrderUsesCurrentPriceAndReservesStock()
    {
        var repo=new Orders();repo.Products.Add(new(){Id=1,Price=10.25m,StockQuantity=5});
        var result=await new OrderService(repo,new Current()).CreateAsync(new(){Items=[new(){ProductId=1,Quantity=2}]},TestContext.Current.CancellationToken);
        Assert.Equal(20.5m,result.TotalAmount);Assert.Equal(10.25m,Assert.Single(result.Items).UnitPrice);Assert.Equal(3,repo.Products[0].StockQuantity);Assert.Equal(1,result.UserId);
    }
    [Fact]
    public async Task InsufficientStockDoesNotCreateOrder()
    {
        var repo=new Orders();repo.Products.Add(new(){Id=1,Price=1,StockQuantity=1});
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new OrderService(repo,new Current()).CreateAsync(new(){Items=[new(){ProductId=1,Quantity=2}]},TestContext.Current.CancellationToken));
        Assert.Equal(400,error.StatusCode);Assert.Empty(repo.Rows);Assert.Equal(1,repo.Products[0].StockQuantity);
    }
    [Fact]
    public async Task MissingProductReturns404()
    {
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new OrderService(new Orders(),new Current()).CreateAsync(new(){Items=[new(){ProductId=99,Quantity=1}]},TestContext.Current.CancellationToken));
        Assert.Equal(404,error.StatusCode);
    }
    [Fact]
    public async Task CustomerCannotCreateForAnotherUser()
    {
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new OrderService(new Orders(),new Current()).CreateAsync(new(){UserId=2,Items=[new(){ProductId=1,Quantity=1}]},TestContext.Current.CancellationToken));
        Assert.Equal(403,error.StatusCode);
    }
    [Fact]
    public async Task CustomerCannotReadAnotherUsersOrder()
    {
        var(repo,_)=Setup();repo.Rows[0].UserId=2;
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new OrderService(repo,new Current()).GetAsync(1,TestContext.Current.CancellationToken));
        Assert.Equal(404,error.StatusCode);
    }
    [Fact]
    public async Task CancellationRestoresStockExactlyOnce()
    {
        var(repo,service)=Setup();
        await service.ChangeStatusAsync(1,new(){Status=OrderStatus.Cancelled},TestContext.Current.CancellationToken);
        await service.ChangeStatusAsync(1,new(){Status=OrderStatus.Cancelled},TestContext.Current.CancellationToken);
        Assert.Equal(10,repo.Products[0].StockQuantity);Assert.Equal(OrderStatus.Cancelled,repo.Rows[0].Status);
    }
    [Theory]
    [InlineData(OrderStatus.Pending,OrderStatus.Completed)]
    [InlineData(OrderStatus.Shipped,OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Completed,OrderStatus.Processing)]
    [InlineData(OrderStatus.Cancelled,OrderStatus.Pending)]
    public async Task InvalidStatusTransitionIsRejected(OrderStatus from,OrderStatus to)
    {
        var(repo,service)=Setup(from);
        var error=await Assert.ThrowsAsync<BusinessException>(()=>service.ChangeStatusAsync(1,new(){Status=to},TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);Assert.Equal(from,repo.Rows[0].Status);Assert.Equal(8,repo.Products[0].StockQuantity);
    }
    [Fact]
    public async Task CustomerCannotAdvanceFulfillmentStatus()
    {
        var(_,service)=Setup(staff:false);
        var error=await Assert.ThrowsAsync<BusinessException>(()=>service.ChangeStatusAsync(1,new(){Status=OrderStatus.Processing},TestContext.Current.CancellationToken));
        Assert.Equal(403,error.StatusCode);
    }
    [Fact]
    public async Task AddingExistingItemPreservesOriginalPrice()
    {
        var(repo,service)=Setup();repo.Products[0].Price=99;
        var result=await service.AddItemAsync(1,new(){ProductId=1,Quantity=1},TestContext.Current.CancellationToken);
        Assert.Equal(30.75m,result.TotalAmount);Assert.Equal(10.25m,Assert.Single(result.Items).UnitPrice);Assert.Equal(7,repo.Products[0].StockQuantity);
    }
    [Fact]
    public async Task ReducingQuantityReturnsOnlyDifferenceToStock()
    {
        var(repo,service)=Setup();
        var result=await service.UpdateItemAsync(1,1,new(){Quantity=1},TestContext.Current.CancellationToken);
        Assert.Equal(9,repo.Products[0].StockQuantity);Assert.Equal(10.25m,result.TotalAmount);
    }
    [Fact]
    public async Task DeletingCancelledOrderDoesNotRestoreAgain()
    {
        var(repo,service)=Setup(OrderStatus.Cancelled);
        await service.DeleteAsync(1,TestContext.Current.CancellationToken);
        Assert.Empty(repo.Rows);Assert.Equal(8,repo.Products[0].StockQuantity);
    }
    [Fact]
    public async Task CannotEditProcessingOrder()
    {
        var(_,service)=Setup(OrderStatus.Processing);
        var error=await Assert.ThrowsAsync<BusinessException>(()=>service.AddItemAsync(1,new(){ProductId=1,Quantity=1},TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);
    }
}

