using StoreFlow.Application.DTOs;

namespace StoreFlow.Application.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync(CancellationToken ct);
    Task<CategoryDto> GetAsync(int id, CancellationToken ct);
    Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct);
    Task UpdateAsync(int id, CategoryRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
