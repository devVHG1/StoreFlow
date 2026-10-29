using System.ComponentModel.DataAnnotations;
using StoreFlow.Domain.Enums;

namespace StoreFlow.Application.DTOs;

public record OrderItemDto(int Id, int ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);
public record OrderDto(int Id, int UserId, DateTime CreatedAt, OrderStatus Status, decimal TotalAmount, List<OrderItemDto> Items, DateTime? ReservationExpiresAtUtc = null);

public class CreateOrderItemDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Вкажіть товар.")]
    public int ProductId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Кількість має бути більшою за нуль.")]
    public int Quantity { get; set; }
}

public class CreateOrderDto : IValidatableObject
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? IdempotencyKey { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Вкажіть коректний id користувача.")]
    public int? UserId { get; set; }
    [Required(ErrorMessage = "Додайте позиції замовлення.")]
    [MinLength(1, ErrorMessage = "Замовлення має містити хоча б одну позицію.")]
    [MaxLength(100, ErrorMessage = "Замовлення може містити до 100 позицій.")]
    public List<CreateOrderItemDto> Items { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items?.Any(x => x is null) == true)
            yield return new ValidationResult("Позиція замовлення не може бути null.", [nameof(Items)]);
    }
}

public class UpdateOrderItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Кількість має бути більшою за нуль.")]
    public int Quantity { get; set; }
}

public class UpdateOrderStatusRequest
{
    [StringLength(500, ErrorMessage = "Причина має містити до 500 символів.")]
    public string? Reason { get; set; }
    [EnumDataType(typeof(OrderStatus), ErrorMessage = "Вкажіть коректний статус замовлення.")]
    public OrderStatus Status { get; set; }
}

