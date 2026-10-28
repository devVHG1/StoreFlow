using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetAllAsync(ProductQuery query, CancellationToken ct);
    Task<ProductDto> GetAsync(int id, CancellationToken ct);
    Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct);
    Task UpdateAsync(int id, ProductRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
