using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wbskt.Infrastructure.Security;

public static class JwtServiceCollectionExtensions
{
    public const string JwksPath = "/.well-known/jwks.json";

    /// <summary>
    /// This host signs tokens as <paramref name="issuer"/>: loads its key pair (<c>Jwt:SigningKey</c>)
    /// and registers <see cref="IJwtService"/>. Pair with <see cref="MapWbsktJwks"/>.
    /// </summary>
    public static IServiceCollection AddWbsktTokenIssuer(this IServiceCollection services, string issuer)
    {
        services.AddSingleton<JwtSigningKeys>();
        services.AddSingleton(sp => new LocalJwtIssuer(issuer, sp.GetRequiredService<JwtSigningKeys>()));
        services.AddSingleton<IJwtService>(sp => new JwtService(sp.GetRequiredService<JwtSigningKeys>(), issuer));
        return services;
    }

    /// <summary>This host accepts tokens signed by <paramref name="issuer"/> for <paramref name="audience"/>, and no others.</summary>
    public static IServiceCollection AddWbsktJwtTrust(this IServiceCollection services, string issuer, string audience)
    {
        services.AddSingleton(sp => JwtTrust.Create(sp.GetRequiredService<IConfiguration>(), issuer, audience, sp.GetService<LocalJwtIssuer>()));
        return services;
    }

    /// <summary>The bearer scheme, validating against the registered <see cref="JwtTrust"/>.</summary>
    public static AuthenticationBuilder AddWbsktJwtBearer(this AuthenticationBuilder builder, Action<JwtBearerOptions>? configure = null)
    {
        builder.AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            configure?.Invoke(options);
        });

        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtTrust>((options, trust) =>
            {
                options.TokenValidationParameters = trust.CreateParameters();
                if (trust.ConfigurationManager is not null)
                {
                    options.ConfigurationManager = trust.ConfigurationManager;
                }
            });

        return builder;
    }

    /// <summary>Publishes this host's public keys, anonymously, for every host that validates its tokens.</summary>
    public static IEndpointConventionBuilder MapWbsktJwks(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(JwksPath, (JwtSigningKeys keys, HttpContext context) =>
            {
                // Validators cache on their own schedule; this only spares a proxy or browser refetching.
                context.Response.Headers.CacheControl = "public, max-age=60";
                return Results.Json(keys.ToJwks());
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
}
