namespace StoreFlow.Domain.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ProductName { get; set; } = string.Empty;
    public bool IsNameBackfilled { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}