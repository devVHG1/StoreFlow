namespace StoreFlow.Application.Services;

public sealed class RequestValidationException(int statusCode, Dictionary<string, string[]> errors)
    : BusinessException(statusCode, "Помилка валідації.")
{
    public Dictionary<string, string[]> Errors { get; } = errors;
    public static RequestValidationException DuplicateEmail() =>
        new(409, new() { ["email"] = ["Користувач із такою адресою вже існує."] });
}
