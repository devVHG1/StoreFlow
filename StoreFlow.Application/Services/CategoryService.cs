using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Application.Services;

public class CategoryService(ICatalogRepository repository) : ICategoryService
{
    public async Task<List<CategoryDto>> GetAllAsync(CancellationToken ct) =>
        (await repository.GetCategoriesAsync(ct)).Select(Map).ToList();

    public async Task<CategoryDto> GetAsync(int id, CancellationToken ct) => Map(await FindAsync(id, ct));

    public async Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct)
    {
        
        var entity = new Category();
        Apply(entity, request);
        repository.AddCategory(entity);
        await repository.SaveAsync(ct);
        return Map(entity);
    }

    public async Task UpdateAsync(int id, CategoryRequest request, CancellationToken ct)
    {
        var entity = await FindAsync(id, ct);
        
        Apply(entity, request);
        await repository.SaveAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var entity = await FindAsync(id, ct);
        if (await repository.CategoryHasProductsAsync(id, ct))
            throw new CatalogException(409, "Категорія містить товари.");
        repository.RemoveCategory(entity);
        await repository.SaveAsync(ct);
    }

    private async Task<Category> FindAsync(int id, CancellationToken ct) =>
        await repository.GetCategoryAsync(id, ct) ?? throw new CatalogException(404, "Категорію не знайдено.");

    

    private static CategoryDto Map(Category entity) => new(entity.Id, entity.Name, entity.Description);

    private static void Apply(Category entity, CategoryRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        
    }
}
