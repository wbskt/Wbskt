using System.Collections.Concurrent;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Wbskt.Auth.Host.Extensions;

internal static class OpenApiExtension
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
            options.Layout = ScalarLayout.Modern;
            options.DarkMode = true;
            options.HiddenClients = false;
            options.DefaultOpenAllTags = true;
            options.ForceThemeMode = ThemeMode.Dark;
            options.Theme = ScalarTheme.Kepler;
            options.ShowDeveloperTools = DeveloperToolsVisibility.Never;
            options.AddPreferredSecuritySchemes("Bearer");
        });
    }
}