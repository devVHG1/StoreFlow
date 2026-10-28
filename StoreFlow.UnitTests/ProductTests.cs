using System.ComponentModel.DataAnnotations;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using Xunit;

namespace StoreFlow.UnitTests;

public class ProductTests
{
    [Fact]
    public async Task CreationRequiresExistingCategory()
    {
        var repo=new Catalog();
        var error=await Assert.ThrowsAsync<CatalogException>(()=>new ProductService(repo).CreateAsync(new(){Name="Item",Price=10,StockQuantity=0,CategoryId=4},TestContext.Current.CancellationToken));
        Assert.Equal(400,error.StatusCode);Assert.Empty(repo.Products);
    }
    [Fact]
    public async Task CreationTrimsTextAndPreservesDecimalPriceAndZeroStock()
    {
        var repo=new Catalog();repo.Categories.Add(new(){Id=1});
        var result=await new ProductService(repo).CreateAsync(new(){Name=" Item ",Description=" Detail ",Price=12.3456m,StockQuantity=0,CategoryId=1},TestContext.Current.CancellationToken);
        Assert.Equal("Item",result.Name);Assert.Equal("Detail",result.Description);Assert.Equal(12.3456m,result.Price);Assert.Equal(0,result.StockQuantity);
    }
    [Fact]
    public async Task MissingProductReturns404()
    {
        var error=await Assert.ThrowsAsync<CatalogException>(()=>new ProductService(new Catalog()).GetAsync(100,TestContext.Current.CancellationToken));
        Assert.Equal(404,error.StatusCode);
    }
    [Fact]
    public async Task ProductUsedInOrderCannotBeDeleted()
    {
        var repo=new Catalog{UsedInOrder=true};repo.Products.Add(new(){Id=1});
        var error=await Assert.ThrowsAsync<CatalogException>(()=>new ProductService(repo).DeleteAsync(1,TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);Assert.Single(repo.Products);
    }
    [Theory]
    [InlineData(0,20,0,false)]
    [InlineData(20,20,1,false)]
    [InlineData(21,20,2,true)]
    public void PaginationMetadataMatchesFilteredCount(int count,int size,int pages,bool next)
    {
        var result=new PagedResult<ProductDto>([],1,size,count);
        Assert.Equal(pages,result.TotalPages);Assert.Equal(next,result.HasNextPage);Assert.False(result.HasPreviousPage);
    }
    [Theory]
    [InlineData(0,20)]
    [InlineData(1,101)]
    [InlineData(int.MaxValue,100)]
    public void InvalidPaginationIsRejected(int page,int size)
    {
        var errors=new List<ValidationResult>();
        var query=new ProductQuery{Page=page,PageSize=size};
        Assert.False(Validator.TryValidateObject(query,new ValidationContext(query),errors,true));
    }
    [Fact]
    public void InvertedPriceRangeReportsMaxPrice()
    {
        var query=new ProductQuery{MinPrice=20,MaxPrice=10};var errors=new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(query,new ValidationContext(query),errors,true));
        Assert.Contains(errors,x=>x.MemberNames.Contains("MaxPrice"));
    }
}

