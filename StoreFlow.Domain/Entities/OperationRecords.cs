using StoreFlow.Domain.Enums;
namespace StoreFlow.Domain.Entities;

public class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public long QuantityChange { get; set; }
    public int BeforeQuantity { get; set; }
    public int AfterQuantity { get; set; }
    public int? ActorId { get; set; }
    public Guid? OrderReference { get; set; }
    public string Reason { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
public class OrderStatusEvent
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public OrderStatus? PreviousStatus { get; set; }
    public OrderStatus Status { get; set; }
    public int? ActorId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
public class OrderRequest
{
    public long Id { get; set; }
    public int ActorId { get; set; }
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}
