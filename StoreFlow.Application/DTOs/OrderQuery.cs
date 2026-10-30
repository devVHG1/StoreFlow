using System.ComponentModel.DataAnnotations;

namespace StoreFlow.Application.DTOs;

public class OrderQuery : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Номер сторінки має бути більшим за нуль.")]
    public int Page { get; set; } = 1;
    [Range(1, 100, ErrorMessage = "Розмір сторінки має бути від 1 до 100.")]
    public int PageSize { get; set; } = 20;
    [RegularExpression("^(Pending|Processing|Shipped|Completed|Cancelled)$", ErrorMessage = "Оберіть коректний статус замовлення.")]
    public string? Status { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Вкажіть коректний id користувача.")]
    public int? UserId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((long)(Page - 1) * PageSize > int.MaxValue)
            yield return new ValidationResult("Номер сторінки завеликий.", [nameof(Page)]);
    }
}
