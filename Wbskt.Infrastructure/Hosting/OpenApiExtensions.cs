using System.Collections.Concurrent;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

namespace Wbskt.Infrastructure.Hosting;

/// <summary>The API reference every host with controllers serves: OpenAPI with the bearer scheme, shown through Scalar.</summary>
public static class OpenApiExtensions
{
    public static void AddCustomOpenApi(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                var securityScheme = new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Please enter JWT."
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new ConcurrentDictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes?["Bearer"] = securityScheme;
                var securityRequirement = new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("Bearer", document),
                        []
                    }
                };

                document.Security ??= new List<OpenApiSecurityRequirement>();
                document.Security?.Add(securityRequirement);
                return Task.CompletedTask;
            });
        });
    }

    public static void MapCustomScalarApiReference(this IEndpointRouteBuilder builder)
    {
        builder.MapScalarApiReference(options =>
        {
            options.HideClientButton = true;
            options.Layout = ScalarLayout.Classic;
            options.DarkMode = true;
            options.HiddenClients = true;
            options.DefaultOpenAllTags = true;
            options.ForceThemeMode = ThemeMode.Dark;
            options.Theme = ScalarTheme.Moon;
            options.ShowDeveloperTools = DeveloperToolsVisibility.Never;
            options.AddPreferredSecuritySchemes("Bearer");
        }).AllowAnonymous(); // Like MapOpenApi, gated on ApiDocs:Enabled and exempt from the default-deny fallback policy.
    }
}
