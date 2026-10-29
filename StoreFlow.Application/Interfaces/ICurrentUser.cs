namespace StoreFlow.Application.Interfaces;

public interface ICurrentUser
{
    int Id { get; }
    int? AuditId => null;
    bool IsStaff { get; }
    bool IsAdmin { get; }
}
