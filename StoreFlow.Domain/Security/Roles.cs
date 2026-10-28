namespace StoreFlow.Domain.Security;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Customer = "Customer";
    public const string Staff = Admin + "," + Manager;
    public static bool IsValid(string role) => role is Admin or Manager or Customer;
}
