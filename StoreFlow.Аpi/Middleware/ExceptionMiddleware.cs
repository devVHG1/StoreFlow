using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreFlow.Application.Services;

namespace StoreFlow.Аpi.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var postgres = error as PostgresException ?? error.InnerException as PostgresException;
            var validation = error as RequestValidationException;
            if (postgres?.SqlState == PostgresErrorCodes.UniqueViolation &&
                postgres.ConstraintName == "IX_Users_NormalizedEmail")
                validation = RequestValidationException.DuplicateEmail();
            if (validation is not null)
            {
                context.Response.Clear();
                await Results.Problem(statusCode: validation.StatusCode, title: validation.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errors"] = validation.Errors,
                        ["traceId"] = context.TraceIdentifier
                    })
                    .ExecuteAsync(context);
                return;
            }
            var (status, title) = error switch
            {
                BusinessException business => (business.StatusCode, business.Message),
                DbUpdateConcurrencyException => (409, "Дані вже змінено. Оновіть сторінку та повторіть запит."),
                _ when postgres?.SqlState == PostgresErrorCodes.UniqueViolation =>
                    (409, "Запис із такими унікальними даними вже існує."),
                _ when postgres?.SqlState == PostgresErrorCodes.ForeignKeyViolation =>
                    (409, "Запис використовується іншими даними або пов’язаний запис уже видалено."),
                _ when postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected =>
                    (409, "Дані змінюються іншим запитом. Повторіть спробу."),
                _ => (500, "Не вдалося обробити запит.")
            };
            if (status == 500) logger.LogError(error, "Помилка обробки запиту {TraceId}", context.TraceIdentifier);
            context.Response.Clear();
            if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
            await Results.Problem(statusCode: status, title: title,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
