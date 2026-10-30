using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Security;

namespace StoreFlow.UnitTests;

internal sealed class Current : ICurrentUser
{
    public int Id { get; set; } = 1;
    public bool IsAdmin { get; set; }
    public bool IsStaff => IsAdmin || Manager;
    public bool Manager { get; set; }
}
internal sealed class Passwords : IPasswordService
{
    public bool Rehash { get; set; }
    public int HashCalls { get; private set; }
    public string Hash(string password) { HashCalls++; return "hashed:" + password; }
    public PasswordCheck Verify(string? hash, string password) => new(hash == "hashed:" + password, Rehash);
}
internal sealed class Tokens : ITokenService
{
    public int Issued { get; private set; }
    public AuthDto Create(User user)
    {
        Issued++;
        return new("unit-token", DateTime.UtcNow.AddMinutes(15),
            new(user.Id,user.FirstName,user.LastName,user.Email,user.Role,user.CreatedAt));
    }
}
internal sealed class Users : IUserRepository
{
    public List<User> Rows { get; } = [];
    public Task<List<User>> GetAllAsync(CancellationToken ct) => Task.FromResult(Rows.ToList());
    public Task<User?> GetAsync(int id,CancellationToken ct) => Task.FromResult(Rows.SingleOrDefault(x=>x.Id==id));
    public Task<User?> GetForUpdateAsync(int id,CancellationToken ct) => GetAsync(id,ct);
    public Task<User?> GetByEmailForUpdateAsync(string email,CancellationToken ct) => Task.FromResult(Rows.SingleOrDefault(x=>x.NormalizedEmail==email));
    public Task<int> CountAdminsAsync(CancellationToken ct) => Task.FromResult(Rows.Count(x=>x.Role==Roles.Admin));
    public void Add(User user) { user.Id=Rows.Count+1; Rows.Add(user); }
    public Task<T> InTransactionAsync<T>(Func<Task<T>> action,CancellationToken ct,bool lockRoles=false) => action();
}
internal sealed class Catalog : ICatalogRepository
{
    public void SetAudit(string reason) { }
    public List<Category> Categories { get; } = [];
    public List<Product> Products { get; } = [];
    public bool UsedInOrder { get; set; }
    public Task<List<Category>> GetCategoriesAsync(CancellationToken ct)=>Task.FromResult(Categories.ToList());
    public Task<Category?> GetCategoryAsync(int id,CancellationToken ct)=>Task.FromResult(Categories.SingleOrDefault(x=>x.Id==id));
    public Task<(List<Product> Items,int TotalCount)> GetProductsAsync(ProductQuery query,CancellationToken ct)=>Task.FromResult((Products.ToList(),Products.Count));
    public Task<Product?> GetProductAsync(int id,CancellationToken ct)=>Task.FromResult(Products.SingleOrDefault(x=>x.Id==id));
    public Task<bool> CategoryHasProductsAsync(int id,CancellationToken ct)=>Task.FromResult(Products.Any(x=>x.CategoryId==id));
    public Task<bool> ProductHasOrderItemsAsync(int id,CancellationToken ct)=>Task.FromResult(UsedInOrder);
    public void AddCategory(Category value) { value.Id=Categories.Count+1;Categories.Add(value); }
    public void AddProduct(Product value) { value.Id=Products.Count+1;Products.Add(value); }
    public void RemoveCategory(Category value)=>Categories.Remove(value);
    public void RemoveProduct(Product value)=>Products.Remove(value);
    public Task SaveAsync(CancellationToken ct)=>Task.CompletedTask;
}
internal sealed class Orders : IOrderRepository
{
    public List<OrderRequest> Requests { get; } = [];
    public void SetAudit(string reason, Guid? reference) { }
    public Task<OrderRequest?> FindRequestAsync(int actorId, string key, CancellationToken ct) => Task.FromResult(Requests.SingleOrDefault(x => x.ActorId == actorId && x.Key == key));
    public void AddRequest(OrderRequest request) => Requests.Add(request);
    public Task<List<int>> FindExpiredAsync(DateTime now, CancellationToken ct) => Task.FromResult(Rows.Where(x => !x.IsDeleted && x.Status == StoreFlow.Domain.Enums.OrderStatus.Pending && x.ReservationExpiresAtUtc <= now).Select(x => x.Id).ToList());
    public List<Order> Rows { get; } = [];
    public List<Product> Products { get; } = [];
    public bool UserExists { get; set; }=true;
    public Task<(List<Order> Items,int TotalCount)> GetAllAsync(OrderQuery query,int? userId,CancellationToken ct)
    {
        var rows=Rows.Where(x=>!x.IsDeleted&&(userId==null||x.UserId==userId));
        if(!string.IsNullOrEmpty(query.Status))rows=rows.Where(x=>x.Status.ToString()==query.Status);
        return Task.FromResult((rows.OrderByDescending(x=>x.Id).Skip((query.Page-1)*query.PageSize).Take(query.PageSize).ToList(),rows.Count()));
    }
    public Task<Order?> GetAsync(int id,CancellationToken ct)=>Task.FromResult(Rows.SingleOrDefault(x=>x.Id==id));
    public Task<Order?> GetForUpdateAsync(int id,CancellationToken ct)=>GetAsync(id,ct);
    public Task<bool> UserExistsAsync(int id,CancellationToken ct)=>Task.FromResult(UserExists);
    public Task<List<Product>> LockProductsAsync(int[] ids,CancellationToken ct)=>Task.FromResult(Products.Where(x=>ids.Contains(x.Id)).ToList());
    public void Add(Order value) { value.Id=Rows.Count+1;int item=1;foreach(var line in value.Items)line.Id=item++;Rows.Add(value); }
    public void Remove(Order value)=>Rows.Remove(value);
    public void RemoveItem(OrderItem value) {}
    public Task<T> InTransactionAsync<T>(Func<Task<T>> action,CancellationToken ct)=>action();
}
