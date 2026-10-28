using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Аpi.Security;

public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly User placeholder = new();
    private readonly string dummyHash;

    public PasswordService() => dummyHash = hasher.HashPassword(placeholder, Guid.NewGuid().ToString("N"));

    public string Hash(string password) => hasher.HashPassword(placeholder, password);
    public PasswordCheck Verify(string? hash, string password)
    {
        try
        {
            var result = hasher.VerifyHashedPassword(placeholder, hash ?? dummyHash, password);
            return new(result != PasswordVerificationResult.Failed, result == PasswordVerificationResult.SuccessRehashNeeded);
        }
        catch (FormatException) { return new(false, false); }
    }
}
