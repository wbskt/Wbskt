using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;

namespace Wbskt.Infrastructure.Security;

public static class JwtServiceCollectionExtensions
{
    public const string JwksPath = "/.well-known/jwks.json";

    /// <summary>
    /// The access token claim naming the sign-in session it belongs to (the refresh token chain), so
    /// ending one session can end its access token too.
    /// </summary>
    public const string JwtSessionClaim = "sid";

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

    /// <summary>
    /// Per-user access-token revocation shared through Redis (<c>ConnectionStrings:Redis</c>), and the
    /// access-token lifetime it is sized for. Hosts that also call <see cref="AddWbsktJwtBearer"/> refuse
    /// revoked user tokens.
    /// </summary>
    public static IServiceCollection AddAccessTokenRevocation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AccessTokenOptions>(configuration.GetSection("Jwt"));
        services.TryAddSingleton(TimeProvider.System);

        TryAddRedis(services, configuration);

        services.AddSingleton(sp => new AccessTokenRevocation(
            sp.GetService<IConnectionMultiplexer>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccessTokenOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AccessTokenRevocation>>()));
        services.AddSingleton<IAccessTokenRevocation>(sp => sp.GetRequiredService<AccessTokenRevocation>());
        services.AddHostedService(sp => sp.GetRequiredService<AccessTokenRevocation>());

        // Rides on the same connection: the auth host announces access changes, the management host
        // drops its cached workspace access when it hears one.
        services.TryAddSingleton(sp => new WorkspaceAccessChanges(
            sp.GetService<IConnectionMultiplexer>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorkspaceAccessChanges>>()));
        return services;
    }

    /// <summary>
    /// Per-device token cutoffs shared through Redis (<c>ConnectionStrings:Redis</c>): the management
    /// host writes them, the socket host checks them when a device connects.
    /// </summary>
    public static IServiceCollection AddClientTokenCutoffs(this IServiceCollection services, IConfiguration configuration)
    {
        TryAddRedis(services, configuration);
        services.TryAddSingleton<IClientTokenCutoffs>(sp => new ClientTokenCutoffs(
            sp.GetService<IConnectionMultiplexer>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClientTokenCutoffs>>()));
        return services;
    }

    private static void TryAddRedis(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            // Not AbortOnConnectFail: Redis being down at startup must not take the host with it -
            // what rides on it degrades to this host alone and catches up once it is back.
            var options = ConfigurationOptions.Parse(redis);
            options.AbortOnConnectFail = false;
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        }
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

                // Composed with, not replacing, whatever the host's own configure delegate set.
                var validated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    await validated(context);
                    if (context.Result is null && IsRevoked(context))
                    {
                        context.Fail("The access token has been revoked.");
                    }
                };
            });

        return builder;
    }

    private static bool IsRevoked(TokenValidatedContext context)
    {
        var revocation = context.HttpContext.RequestServices.GetService<IAccessTokenRevocation>();
        if (revocation is null || context.SecurityToken is not JsonWebToken token)
        {
            return false;
        }

        // Read from the token itself rather than the principal, so inbound claim mapping cannot rename it.
        if (token.TryGetPayloadValue<string>(JwtSessionClaim, out var sid)
            && Guid.TryParse(sid, out var sessionId)
            && revocation.IsSessionRevoked(sessionId))
        {
            return true;
        }

        var subject = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(subject, out var userId) && revocation.IsRevoked(userId, token.IssuedAt);
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
