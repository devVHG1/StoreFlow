using System.ComponentModel.DataAnnotations;

namespace StoreFlow.Аpi.Security;

public class JwtOptions
{
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;
    [Required]
    public string Issuer { get; set; } = "StoreFlow";
    [Required]
    public string Audience { get; set; } = "StoreFlow.Client";
    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 15;
}
