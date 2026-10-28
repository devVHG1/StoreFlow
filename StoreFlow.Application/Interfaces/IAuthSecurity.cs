using StoreFlow.Application.DTOs;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Application.Interfaces;

public record PasswordCheck(bool IsValid, bool NeedsRehash);
public interface IPasswordService
{
    string Hash(string password);
    PasswordCheck Verify(string? hash, string password);
}
public interface ITokenService
{
    AuthDto Create(User user);
}
