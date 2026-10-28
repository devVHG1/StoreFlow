using System.ComponentModel.DataAnnotations;

namespace StoreFlow.Application.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class PositivePriceAttribute : ValidationAttribute
{
    public PositivePriceAttribute() => ErrorMessage = "Ціна має бути більшою за нуль.";
    public override bool IsValid(object? value) => value is null || value is decimal price && price > 0;
}
