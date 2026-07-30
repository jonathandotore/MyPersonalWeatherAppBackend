using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace WeatherApp.API.OpenApi;

/// <summary>Adiciona o security scheme Bearer ao documento OpenAPI, para o botão
/// "Authorize" do Scalar funcionar contra os endpoints protegidos.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };

        var requisito = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        };

        foreach (var caminho in document.Paths.Values)
        {
            if (caminho.Operations is null)
            {
                continue;
            }

            foreach (var operacao in caminho.Operations.Values)
            {
                operacao.Security ??= [];
                operacao.Security.Add(requisito);
            }
        }

        return Task.CompletedTask;
    }
}
