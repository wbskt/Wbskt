using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Constants;

namespace Wbskt.Infrastructure.Hosting;

/// <summary>
/// What every Wbskt host does the same way: the start of <c>Main</c>, default-deny, forwarded
/// headers, CORS, the per-IP limiter and the middleware order. Each <c>Program</c> keeps only what is
/// its own.
/// </summary>
public static class WbsktHost
{
    /// <summary>
    /// Traefik's pinned addresses on the edge and backend networks in deploy/compose/docker-compose.yml,
    /// used when <c>ForwardedHeaders:KnownProxies</c> is not set.
    /// </summary>
    public static readonly string[] DefaultKnownProxies = ["172.28.0.2", "172.28.1.2"];

    /// <summary>
    /// The builder every host starts from: log location and name, the shared configuration files
    /// named, and Serilog.
    /// </summary>
    /// <param name="program">The host's <c>Program</c>; its namespace names the log.</param>
    public static WebApplicationBuilder CreateBuilder(Type program, string[] args, params string[] configurationFiles)
    {
        string programDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);
        Environment.SetEnvironmentVariable(Logging.LogPath, programDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, program.Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.AddSharedConfiguration(configurationFiles);
        builder.Host.UseSerilog(builder.CreateSerilog());
        return builder;
    }

    /// <summary>
    /// Every endpoint requires a signed-in caller unless it says <c>[AllowAnonymous]</c>, so a
    /// controller added without stating its posture is refused rather than served anonymously.
    /// </summary>
    public static IServiceCollection AddDefaultDenyAuthorization(this IServiceCollection services, Action<AuthorizationOptions>? configure = null)
    {
        return services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from the proxies in
    /// <c>ForwardedHeaders:KnownProxies</c> only (Traefik's addresses by default).
    /// </summary>
    /// <remarks>
    /// The proxies are added one by one: a nested <c>KnownProxies = { }</c> initializer adds nothing,
    /// so the loopback defaults would survive and Traefik's headers be ignored. The lists are not
    /// cleared either, which would trust the headers from any container on the network.
    /// </remarks>
    public static IServiceCollection AddWbsktForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        string[] proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() is { Length: > 0 } configured
            ? configured
            : DefaultKnownProxies;

        services.AddSingleton<ForwardedHeadersRegistered>();
        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (string proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
    }

    /// <summary>
    /// The default CORS policy: the origins from <see cref="CorsOriginsConfiguration.GetCorsAllowedOrigins"/>,
    /// any origin in Development when none is configured, and none otherwise. With credentials, so a
    /// console on another origin can send the refresh cookie (which rules out a literal "*").
    /// </summary>
    /// <param name="exposedHeaders">Response headers script must be able to read (AllowAnyHeader covers request headers only).</param>
    public static IServiceCollection AddWbsktCors(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, params string[] exposedHeaders)
    {
        return services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            var allowedOrigins = configuration.GetCorsAllowedOrigins();
            if (allowedOrigins is { Length: > 0 })
            {
                policy.SetIsOriginAllowed(allowedOrigins.IsCorsOriginAllowed).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
            else if (environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }

            if (exposedHeaders.Length > 0)
            {
                policy.WithExposedHeaders(exposedHeaders);
            }
        }));
    }

    /// <summary>
    /// The middleware every host runs, in one order: forwarded headers (so everything after sees the
    /// real client address), exception handling, CORS, the rate limiter (before authentication, so a
    /// flood is refused before a token is validated), authentication, the identity middleware, and
    /// authorization. A step whose services the host did not register is skipped.
    /// </summary>
    public static WebApplication UseWbsktPipeline(this WebApplication app)
    {
        // Not IConfigureOptions<ForwardedHeadersOptions>: the web host always registers one of those.
        if (app.Services.GetService<ForwardedHeadersRegistered>() is not null)
        {
            app.UseForwardedHeaders();
        }

        app.UseMiddleware<GlobalExceptionMiddleware>();

        if (app.Services.GetService<ICorsService>() is not null)
        {
            app.UseCors();
        }

        if (app.Services.GetService<IConfigureOptions<RateLimiterOptions>>() is not null)
        {
            app.UseRateLimiter();
        }

        app.UseAuthentication();
        if (app.Services.GetService<IIdentityService>() is not null)
        {
            app.UseMiddleware<IdentityMiddleware>();
        }

        app.UseAuthorization();
        return app;
    }

    private sealed class ForwardedHeadersRegistered;
}
