using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using Xunit;
namespace StoreFlow.UnitTests;

public class OrderSnapshotTests
{
    [Fact]
    public async Task ExistingPositionRetainsOriginalNameAndPriceAfterRename()
    {
        var repository=new Orders();
        var product=new Product { Id=1,Name="Початкова назва",Price=10,StockQuantity=10 };
        repository.Products.Add(product);
        var service=new OrderService(repository,new Current());
        var order=await service.CreateAsync(new(){Items=[new(){ProductId=1,Quantity=1}]},TestContext.Current.CancellationToken);
        product.Name="Нова назва";product.Price=99;
        order=await service.AddItemAsync(order.Id,new(){ProductId=1,Quantity=1},TestContext.Current.CancellationToken);
        var item=Assert.Single(order.Items);
        Assert.Equal("Початкова назва",item.ProductName);
        Assert.Equal(10,item.UnitPrice);
        Assert.Equal(20,order.TotalAmount);
        Assert.False(item.IsNameBackfilled);
        var latest=await service.CreateAsync(new(){Items=[new(){ProductId=1,Quantity=1}]},TestContext.Current.CancellationToken);
        Assert.Equal("Нова назва",Assert.Single(latest.Items).ProductName);
    }

    [Fact]
    public async Task NewPositionCapturesNameWhenAdded()
    {
        var repository=new Orders();
        repository.Products.Add(new(){Id=1,Name="Один",Price=1,StockQuantity=5});
        repository.Products.Add(new(){Id=2,Name="Два",Price=2,StockQuantity=5});
        var service=new OrderService(repository,new Current());
        var order=await service.CreateAsync(new(){Items=[new(){ProductId=1,Quantity=1}]},TestContext.Current.CancellationToken);
        order=await service.AddItemAsync(order.Id,new(){ProductId=2,Quantity=1},TestContext.Current.CancellationToken);
        Assert.Equal("Два",order.Items.Single(x=>x.ProductId==2).ProductName);
    }
}
