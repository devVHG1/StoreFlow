using System.ComponentModel.DataAnnotations;
using StoreFlow.Application.Validation;

namespace StoreFlow.Application.DTOs;

public record UserDto(int Id, string FirstName, string LastName, string Email, string Role, DateTime CreatedAt);
public record AuthDto(string AccessToken, DateTime ExpiresAtUtc, UserDto User, string TokenType = "Bearer");

public class RegisterDto
{
    [Required(ErrorMessage = "Вкажіть ім’я."), StringLength(100, ErrorMessage = "Ім’я має містити до 100 символів.")]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть прізвище."), StringLength(100, ErrorMessage = "Прізвище має містити до 100 символів.")]
    public string LastName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть email."), ValidEmail, StringLength(254, ErrorMessage = "Email має містити до 254 символів.")]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть пароль."), StringLength(128, MinimumLength = 12, ErrorMessage = "Пароль має містити від 12 до 128 символів.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    [Required(ErrorMessage = "Вкажіть email."), ValidEmail, StringLength(254, ErrorMessage = "Email має містити до 254 символів.")]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть пароль."), StringLength(128, ErrorMessage = "Пароль має містити до 128 символів.")]
    public string Password { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    [Required(ErrorMessage = "Вкажіть ім’я."), StringLength(100, ErrorMessage = "Ім’я має містити до 100 символів.")]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть прізвище."), StringLength(100, ErrorMessage = "Прізвище має містити до 100 символів.")]
    public string LastName { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [Required(ErrorMessage = "Вкажіть поточний пароль."), StringLength(128, ErrorMessage = "Пароль має містити до 128 символів.")]
    public string CurrentPassword { get; set; } = string.Empty;
    [Required(ErrorMessage = "Вкажіть новий пароль."), StringLength(128, MinimumLength = 12, ErrorMessage = "Пароль має містити від 12 до 128 символів.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangeRoleDto
{
    [Required(ErrorMessage = "Вкажіть роль."), RegularExpression("^(Admin|Manager|Customer)$", ErrorMessage = "Оберіть Admin, Manager або Customer.")]
    public string Role { get; set; } = string.Empty;
}
