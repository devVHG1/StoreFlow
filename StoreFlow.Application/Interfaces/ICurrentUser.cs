namespace StoreFlow.Application.Interfaces;

public interface ICurrentUser
{
    int Id { get; }
    bool IsStaff { get; }
    bool IsAdmin { get; }
}
