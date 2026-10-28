namespace StoreFlow.Application.Services;

public class CatalogException(int statusCode, string message) : BusinessException(statusCode, message);
