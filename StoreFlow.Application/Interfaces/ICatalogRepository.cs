using StoreFlow.Domain.Entities;
using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface ICatalogRepository
{
    Task<List<Category>> GetCategoriesAsync(CancellationToken ct);
    Task<Category?> GetCategoryAsync(int id, CancellationToken ct);
    Task<(List<Product> Items, int TotalCount)> GetProductsAsync(ProductQuery query, CancellationToken ct);
    Task<Product?> GetProductAsync(int id, CancellationToken ct);
    Task<bool> CategoryHasProductsAsync(int id, CancellationToken ct);
    Task<bool> ProductHasOrderItemsAsync(int id, CancellationToken ct);
    void AddCategory(Category category);
    void AddProduct(Product product);
    void RemoveCategory(Category category);
    void RemoveProduct(Product product);
    void SetAudit(string reason);
    Task SaveAsync(CancellationToken ct);
}
