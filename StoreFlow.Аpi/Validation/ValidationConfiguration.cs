using Microsoft.AspNetCore.Mvc;

namespace StoreFlow.Аpi.Validation;

public static class ValidationConfiguration
{
    public static IMvcBuilder AddValidationResponses(this IMvcBuilder mvc) => mvc
        .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
                .GroupBy(x => FieldName(x.Key))
                .ToDictionary(group => group.Key, group => group.SelectMany(x => x.Value!.Errors)
                    .Select(error =>
                    {
                        var text = error.ErrorMessage;
                        if (error.Exception is not null || text.StartsWith("The JSON") || text.Contains("Path:"))
                            return "Некоректне значення або формат JSON.";
                        if (string.IsNullOrWhiteSpace(text) || !text.Any(c => c is >= '\u0400' and <= '\u04ff'))
                            return "Вкажіть коректне значення поля.";
                        return text;
                    }).Distinct().ToArray());
            var problem = new ValidationProblemDetails(errors)
            {
                Status = 400, Title = "Помилка валідації.", Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            var result = new BadRequestObjectResult(problem);
            result.ContentTypes.Add("application/problem+json");
            return result;
        });

    private static string FieldName(string key)
    {
        if (string.IsNullOrEmpty(key) || key is "$" or "request") return "body";
        return string.Join('.', key.TrimStart('$', '.').Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}
