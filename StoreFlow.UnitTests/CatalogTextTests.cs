using System.ComponentModel.DataAnnotations;
using StoreFlow.Application.DTOs;
using Xunit;
namespace StoreFlow.UnitTests;

public class CatalogTextTests
{
    [Theory]
    [InlineData(200,4000,true)]
    [InlineData(201,0,false)]
    [InlineData(1,4001,false)]
    public void TextLimitsAcceptBoundaryAndRejectOverflow(int nameLength, int descriptionLength, bool expected)
    {
        foreach (CategoryRequest request in new CategoryRequest[] { new(), new ProductRequest { Price = 1, StockQuantity = 0, CategoryId = 1 } })
        {
            request.Name = new string('x',nameLength);
            request.Description = new string('x',descriptionLength);
            Assert.Equal(expected, Validator.TryValidateObject(request,new ValidationContext(request),[],true));
        }
    }
}
