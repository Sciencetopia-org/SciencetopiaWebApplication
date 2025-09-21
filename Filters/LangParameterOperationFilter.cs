using System.Linq;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SciencetopiaWebApplication.Filters
{
    /// <summary>
    /// Ensures the 'lang' query parameter appears in Swagger when present on action signature.
    /// Works around cases where Swashbuckle may omit optional query params.
    /// </summary>
    public class LangParameterOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // If the action method has a parameter named 'lang', ensure it's documented
            var hasLangParam = context.MethodInfo
                .GetParameters()
                .Any(p => string.Equals(p.Name, "lang", System.StringComparison.OrdinalIgnoreCase));

            if (!hasLangParam) return;

            var alreadyExists = operation.Parameters
                .Any(p => string.Equals(p.Name, "lang", System.StringComparison.OrdinalIgnoreCase));
            if (alreadyExists) return;

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "lang",
                In = ParameterLocation.Query,
                Required = false,
                Description = "Language code (e.g., zh, en-US). Defaults to server configured value.",
                Schema = new OpenApiSchema
                {
                    Type = "string",
                    Default = new OpenApiString("zh")
                }
            });
        }
    }
}

