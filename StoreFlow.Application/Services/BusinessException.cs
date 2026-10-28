namespace StoreFlow.Application.Services;

public class BusinessException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
