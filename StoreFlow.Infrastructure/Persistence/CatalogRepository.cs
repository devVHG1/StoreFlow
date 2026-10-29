using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;
using StoreFlow.Application.DTOs;

namespace StoreFlow.Infrastructure.Persistence;

public class CatalogRepository(StoreFlowDbContext db) : ICatalogRepository
{
    public Task<List<Category>> GetCategoriesAsync(CancellationToken ct) =>
        db.Categories.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    public Task<Category?> GetCategoryAsync(int id, CancellationToken ct) =>
        db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<(List<Product> Items, int TotalCount)> GetProductsAsync(ProductQuery query, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var literal = query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            products = products.Where(x => EF.Functions.ILike(x.Name, "%" + literal + "%", "\\"));
        }
        if (query.CategoryId is int categoryId) products = products.Where(x => x.CategoryId == categoryId);
        if (query.MinPrice is decimal minPrice) products = products.Where(x => x.Price >= minPrice);
        if (query.MaxPrice is decimal maxPrice) products = products.Where(x => x.Price <= maxPrice);
        var descending = query.SortDirection == "desc";
        IOrderedQueryable<Product> ordered = query.SortBy switch
        {
            "name" => descending ? products.OrderByDescending(x => x.Name) : products.OrderBy(x => x.Name),
            "price" => descending ? products.OrderByDescending(x => x.Price) : products.OrderBy(x => x.Price),
            "stockQuantity" => descending ? products.OrderByDescending(x => x.StockQuantity) : products.OrderBy(x => x.StockQuantity),
            _ => descending ? products.OrderByDescending(x => x.Id) : products.OrderBy(x => x.Id)
        };
        if (query.SortBy != "id") ordered = ordered.ThenBy(x => x.Id);

        // Кількість і сторінка читаються з одного знімка даних.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var total = await products.CountAsync(ct);
        var items = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return (items, total);
    }
    public Task<Product?> GetProductAsync(int id, CancellationToken ct) =>
        db.Products.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> CategoryHasProductsAsync(int id, CancellationToken ct) =>
        db.Products.AnyAsync(x => x.CategoryId == id, ct);
    public Task<bool> ProductHasOrderItemsAsync(int id, CancellationToken ct) =>
        db.OrderItems.AnyAsync(x => x.ProductId == id, ct);
    public void AddCategory(Category category) => db.Categories.Add(category);
    public void AddProduct(Product product) => db.Products.Add(product);
    public void RemoveCategory(Category category) => db.Categories.Remove(category);
    public void RemoveProduct(Product product) => db.Products.Remove(product);
    public void SetAudit(string reason) => db.AuditReason = reason;
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
