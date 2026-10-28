using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace StoreFlow.Application.DTOs;

public class ProductQuery : IValidatableObject
{
    [StringLength(200, ErrorMessage = "Пошуковий запит має містити до 200 символів.")]
    public string? Search { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Вкажіть коректний id категорії.")]
    public int? CategoryId { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ParseLimitsInInvariantCulture = true, ErrorMessage = "Мінімальна ціна не може бути від’ємною.")]
    public decimal? MinPrice { get; set; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335", ParseLimitsInInvariantCulture = true, ErrorMessage = "Максимальна ціна не може бути від’ємною.")]
    public decimal? MaxPrice { get; set; }
    [Required, RegularExpression("^(id|name|price|stockQuantity)$", ErrorMessage = "Сортуйте за id, name, price або stockQuantity.")]
    [DefaultValue("id")]
    public string SortBy { get; set; } = "id";
    [Required, RegularExpression("^(asc|desc)$", ErrorMessage = "Оберіть напрям asc або desc.")]
    [DefaultValue("asc")]
    public string SortDirection { get; set; } = "asc";
    [Range(1, int.MaxValue, ErrorMessage = "Номер сторінки має бути більшим за нуль.")]
    [DefaultValue(1)]
    public int Page { get; set; } = 1;
    [Range(1, 100, ErrorMessage = "Розмір сторінки має бути від 1 до 100.")]
    [DefaultValue(20)]
    public int PageSize { get; set; } = 20;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
            yield return new ValidationResult("Максимальна ціна не може бути меншою за мінімальну.", [nameof(MaxPrice)]);
        if ((long)(Page - 1) * PageSize > int.MaxValue)
            yield return new ValidationResult("Номер сторінки завеликий.", [nameof(Page)]);
    }
}

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => TotalPages > 0 && Page > 1;
}
