using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Application.Services;

public class ProductService(ICatalogRepository repository) : IProductService
{
    public async Task<PagedResult<ProductDto>> GetAllAsync(ProductQuery query, CancellationToken ct)
    {
        var result = await repository.GetProductsAsync(query, ct);
        return new(result.Items.Select(Map).ToList(), query.Page, query.PageSize, result.TotalCount);
    }

    public async Task<ProductDto> GetAsync(int id, CancellationToken ct) => Map(await FindAsync(id, ct));

    public async Task<ProductDto> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        await ValidateCategoryAsync(request.CategoryId!.Value, ct);
        var entity = new Product();
        Apply(entity, request);
        repository.SetAudit("ProductCreated");
        repository.AddProduct(entity);
        await repository.SaveAsync(ct);
        return Map(entity);
    }

    public async Task UpdateAsync(int id, ProductRequest request, CancellationToken ct)
    {
        var entity = await FindAsync(id, ct);
        await ValidateCategoryAsync(request.CategoryId!.Value, ct);
        if (request.Version is null)
            throw new RequestValidationException(400, new() { ["version"] = ["Передайте поточну версію товару."] });
        if (request.Version != entity.Version)
            throw new BusinessException(409, "Товар уже змінено. Прочитайте актуальні дані.");
        if (entity.StockQuantity != request.StockQuantity)
        {
            if (string.IsNullOrWhiteSpace(request.StockChangeReason))
                throw new RequestValidationException(400, new() { ["stockChangeReason"] = ["Вкажіть причину коригування залишку."] });
            repository.SetAudit("ManualAdjustment: " + request.StockChangeReason.Trim());
        }
        Apply(entity, request);
        await repository.SaveAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var entity = await FindAsync(id, ct);
        if (await repository.ProductHasOrderItemsAsync(id, ct))
            throw new CatalogException(409, "Товар уже використовується в замовленнях.");
        repository.RemoveProduct(entity);
        await repository.SaveAsync(ct);
    }

    private async Task<Product> FindAsync(int id, CancellationToken ct) =>
        await repository.GetProductAsync(id, ct) ?? throw new CatalogException(404, "Товар не знайдено.");

    private async Task ValidateCategoryAsync(int id, CancellationToken ct)
    {
        if (await repository.GetCategoryAsync(id, ct) is null)
            throw new CatalogException(400, "Вказано неіснуючу категорію.");
    }

    private static ProductDto Map(Product entity) => new(entity.Id, entity.Name, entity.Description, entity.Price, entity.StockQuantity, entity.CategoryId, entity.Version);

    private static void Apply(Product entity, ProductRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.Price = request.Price!.Value;
        entity.StockQuantity = request.StockQuantity!.Value;
        entity.CategoryId = request.CategoryId!.Value;
    }
}
