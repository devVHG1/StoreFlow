using System.ComponentModel.DataAnnotations;
using StoreFlow.Application.Validation;

namespace StoreFlow.Application.DTOs;

public record CategoryDto(int Id, string Name, string? Description);
public record ProductDto(int Id, string Name, string? Description, decimal Price, int StockQuantity, int CategoryId, uint Version = 0);

public class CategoryRequest
{
    [Required(ErrorMessage = "Вкажіть назву.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ProductRequest : CategoryRequest
{
    public uint? Version { get; set; }
    [StringLength(500, ErrorMessage = "Причина має містити до 500 символів.")]
    public string? StockChangeReason { get; set; }
    [Required(ErrorMessage = "Вкажіть ціну."), PositivePrice]
    public decimal? Price { get; set; }
    [Range(0, int.MaxValue, ErrorMessage = "Залишок не може бути від’ємним.")]
    [Required(ErrorMessage = "Вкажіть залишок товару.")]
    public int? StockQuantity { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Вкажіть категорію.")]
    [Required(ErrorMessage = "Вкажіть категорію.")]
    public int? CategoryId { get; set; }
}
