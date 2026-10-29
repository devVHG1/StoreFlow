using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using StoreFlow.Application.Interfaces;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Services;
using StoreFlow.Domain.Enums;
using StoreFlow.Infrastructure.Persistence;
using StoreFlow.Аpi.Middleware;
using StoreFlow.Аpi.Security;
using StoreFlow.Аpi.Validation;

var builder = WebApplication.CreateBuilder(args);

// Підключаємо базу та сервіси.
builder.Services.AddDbContext<StoreFlowDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddControllers().AddValidationResponses().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter<OrderStatus>(allowIntegerValues: false)));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddStoreFlowAuth(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<StoreFlow.Аpi.ReservationWorker>();

var app = builder.Build();
if (args.Contains("--migrate"))
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StoreFlowDbContext>().Database.MigrateAsync();
        Console.WriteLine("Міграції застосовано.");
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Не вдалося застосувати міграції. Перевірте доступність PostgreSQL та підключення.");
        Environment.ExitCode = 1;
    }
    return;
}
if (args.Contains("--bootstrap-admin"))
{
    try
    {
        var request = new RegisterDto
        {
            FirstName = "StoreFlow", LastName = "Admin",
            Email = builder.Configuration["BootstrapAdmin:Email"] ?? string.Empty,
            Password = builder.Configuration["BootstrapAdmin:Password"] ?? string.Empty
        };
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuthService>().BootstrapAdminAsync(request, CancellationToken.None);
        Console.WriteLine("Адміністратора створено.");
    }
    catch (Exception error) when (error is BusinessException or ValidationException)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Не вдалося створити адміністратора. Перевірте підключення та міграції.");
        Environment.ExitCode = 1;
    }
    return;
}
app.UseMiddleware<ExceptionMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var context = statusContext.HttpContext;
    await Results.Problem(statusCode: context.Response.StatusCode,
        title: context.Response.StatusCode switch
        {
            401 => "Потрібно увійти.",
            403 => "Недостатньо прав.",
            404 => "Ресурс не знайдено.",
            _ => "Не вдалося виконати запит."
        },
        extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
});

// Swagger доступний під час розробки.
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", async (StoreFlowDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "Healthy" }) : Results.StatusCode(503)).AllowAnonymous();
app.MapControllers();
app.Run();

