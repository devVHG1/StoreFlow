using StoreFlow.Application.DTOs;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Security;
using Xunit;

namespace StoreFlow.UnitTests;

public class AuthTests
{
    private static RegisterDto Registration(string email="User@Example.com")=>new(){FirstName=" Test ",LastName=" User ",Email=email,Password="test-password"};
    private static User Account(int attempts=0)=>new(){Id=1,Email="User@Example.com",NormalizedEmail="user@example.com",PasswordHash="hashed:test-password",FailedLoginAttempts=attempts};
    [Fact]
    public async Task RegistrationNormalizesIdentityAndAlwaysCreatesCustomer()
    {
        var users=new Users();var tokens=new Tokens();
        var result=await new AuthService(users,new Passwords(),tokens,new Current()).RegisterAsync(Registration(" User@Example.com "),TestContext.Current.CancellationToken);
        var saved=Assert.Single(users.Rows);
        Assert.Equal("user@example.com",saved.NormalizedEmail);
        Assert.Equal("User@Example.com",saved.Email);
        Assert.Equal("Test",saved.FirstName);
        Assert.Equal(Roles.Customer,saved.Role);
        Assert.NotEqual("test-password",saved.PasswordHash);
        Assert.Equal(saved.Id,result.User.Id);Assert.Equal(1,tokens.Issued);
    }
    [Fact]
    public async Task DuplicateEmailDoesNotCreateAccountOrToken()
    {
        var users=new Users();users.Rows.Add(Account());var tokens=new Tokens();
        var error=await Assert.ThrowsAsync<RequestValidationException>(()=>new AuthService(users,new Passwords(),tokens,new Current()).RegisterAsync(Registration(" USER@EXAMPLE.COM "),TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);Assert.Contains("email",error.Errors.Keys);Assert.Single(users.Rows);Assert.Equal(0,tokens.Issued);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndWrongPasswordHaveSameUnauthorizedStatus(bool exists)
    {
        var users=new Users();if(exists)users.Rows.Add(Account());
        var tokens=new Tokens();
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new AuthService(users,new Passwords(),tokens,new Current()).LoginAsync(new(){Email="user@example.com",Password="wrong"},TestContext.Current.CancellationToken));
        Assert.Equal(401,error.StatusCode);Assert.Equal(0,tokens.Issued);
    }
    [Fact]
    public async Task FifthFailedLoginLocksAccountWithoutIssuingToken()
    {
        var users=new Users();var user=Account(4);users.Rows.Add(user);var tokens=new Tokens();
        await Assert.ThrowsAsync<BusinessException>(()=>new AuthService(users,new Passwords(),tokens,new Current()).LoginAsync(new(){Email=user.Email,Password="wrong"},TestContext.Current.CancellationToken));
        Assert.True(user.LockoutEndUtc>DateTime.UtcNow.AddMinutes(14));
        Assert.Equal(0,user.FailedLoginAttempts);Assert.Equal(0,tokens.Issued);
    }
    [Fact]
    public async Task LockedAccountCannotLoginEvenWithCorrectPassword()
    {
        var users=new Users();var user=Account();user.LockoutEndUtc=DateTime.UtcNow.AddMinutes(10);users.Rows.Add(user);
        var tokens=new Tokens();
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new AuthService(users,new Passwords(),tokens,new Current()).LoginAsync(new(){Email=user.Email,Password="test-password"},TestContext.Current.CancellationToken));
        Assert.Equal(401,error.StatusCode);Assert.Equal(0,tokens.Issued);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulLoginResetsAttemptsAndAllowsRehash(bool rehash)
    {
        var users=new Users();var user=Account(3);user.LockoutEndUtc=DateTime.UtcNow.AddMinutes(-1);users.Rows.Add(user);
        var passwords=new Passwords{Rehash=rehash};
        var result=await new AuthService(users,passwords,new Tokens(),new Current()).LoginAsync(new(){Email="USER@EXAMPLE.COM",Password="test-password"},TestContext.Current.CancellationToken);
        Assert.Equal(rehash?1:0,passwords.HashCalls);
        Assert.Null(user.LockoutEndUtc);Assert.Equal(0,user.FailedLoginAttempts);Assert.Equal(user.Id,result.User.Id);
    }
    [Fact]
    public async Task LogoutInvalidatesPreviouslyIssuedTokens()
    {
        var users=new Users();var user=Account();user.TokenVersion=8;users.Rows.Add(user);
        await new AuthService(users,new Passwords(),new Tokens(),new Current()).LogoutAsync(TestContext.Current.CancellationToken);
        Assert.Equal(9,user.TokenVersion);
    }
    [Fact]
    public async Task BootstrapRefusesSecondAdmin()
    {
        var users=new Users();users.Rows.Add(new(){Id=1,Role=Roles.Admin});
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new AuthService(users,new Passwords(),new Tokens(),new Current()).BootstrapAdminAsync(Registration(),TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);Assert.Single(users.Rows);
    }
    [Fact]
    public async Task LastAdminCannotBeDemoted()
    {
        var users=new Users();var admin=Account();admin.Role=Roles.Admin;users.Rows.Add(admin);
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new UserService(users,new Passwords(),new Current{IsAdmin=true}).ChangeRoleAsync(1,new(){Role=Roles.Customer},TestContext.Current.CancellationToken));
        Assert.Equal(409,error.StatusCode);Assert.Equal(Roles.Admin,admin.Role);
    }
    [Fact]
    public async Task RoleChangeInvalidatesOldTokens()
    {
        var users=new Users();users.Rows.Add(Account());
        var result=await new UserService(users,new Passwords(),new Current{IsAdmin=true}).ChangeRoleAsync(1,new(){Role=Roles.Manager},TestContext.Current.CancellationToken);
        Assert.Equal(Roles.Manager,result.Role);Assert.Equal(1,users.Rows[0].TokenVersion);
    }
    [Fact]
    public async Task CustomerCannotListUsers()
    {
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new UserService(new Users(),new Passwords(),new Current()).GetAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(403,error.StatusCode);
    }
    [Fact]
    public async Task WrongCurrentPasswordDoesNotInvalidateSession()
    {
        var users=new Users();var user=Account();users.Rows.Add(user);
        var error=await Assert.ThrowsAsync<BusinessException>(()=>new UserService(users,new Passwords(),new Current()).ChangePasswordAsync(new(){CurrentPassword="wrong",NewPassword="new-password"},TestContext.Current.CancellationToken));
        Assert.Equal(400,error.StatusCode);Assert.Equal(0,user.TokenVersion);Assert.Equal("hashed:test-password",user.PasswordHash);
    }
}


