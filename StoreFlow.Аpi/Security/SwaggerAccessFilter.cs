using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace StoreFlow.Аpi.Security;

public class SwaggerAccessFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var anonymous = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any() ||
            context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() ||
            context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true;
        if (anonymous) operation.Security = [new OpenApiSecurityRequirement()];
        foreach (var parameter in operation.Parameters.Where(x => x.In == ParameterLocation.Query && x.Schema.Default is not null))
            parameter.Required = false;
    }
}
