using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
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
    /// <summary>How long a browser may reuse a CORS preflight: two hours, the most Chrome honours.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromHours(2);

    /// <summary>
    /// The folder a host keeps its own files in (its log file): <c>Wbskt</c> under the user's
    /// config folder, which is created if it is missing. Without <c>Create</c>, .NET answers an empty
    /// string for a config folder that does not exist yet, and the path silently becomes the working
    /// directory, which the container's unprivileged user cannot write.
    /// </summary>
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        Application.AppFolderName);

    /// <summary>
    /// Traefik's pinned addresses on the edge and backend networks in deploy/compose/docker-compose.yml,
    /// used when <c>ForwardedHeaders:KnownProxies</c> is not set.
    /// </summary>
    public static readonly string[] DefaultKnownProxies = ["172.28.0.2", "172.28.1.2"];

    /// <summary>
    /// The builder every host starts from: log location and name, the shared configuration files
    /// named, Serilog and the host's <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="program">The host's <c>Program</c>; its namespace names the log.</param>
    public static WebApplicationBuilder CreateBuilder(Type program, string[] args, params string[] configurationFiles)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, DataFolder);
        Environment.SetEnvironmentVariable(Logging.LogName, program.Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });

        builder.AddSharedConfiguration(configurationFiles);
        builder.Host.UseSerilog(builder.CreateSerilog());

        // The one clock of the host; everything that reads the time takes it, so a test can fix it.
        builder.Services.AddSingleton(TimeProvider.System);
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

            // Without a max age a browser keeps a preflight for 5 seconds, so nearly every console call
            // paid an extra round trip first. Two hours is Chrome's ceiling; browsers cache per URL.
            policy.SetPreflightMaxAge(PreflightMaxAge);
        }));
    }

    /// <summary>
    /// The middleware every host runs, in one order: the internal route guard (which needs the forwarded
    /// headers as they arrived), forwarded headers (so everything after sees the real client address),
    /// exception handling, Problem Details for bare error statuses, CORS, the rate limiter (before
    /// authentication, so a flood is refused before a token is validated), authentication, the identity
    /// middleware, the per-user budget (after authentication, since it partitions on the user), and
    /// authorization. A step whose services the host did not register is skipped.
    /// </summary>
    public static WebApplication UseWbsktPipeline(this WebApplication app)
    {
        app.UseMiddleware<InternalRouteGuardMiddleware>();

        // Not IConfigureOptions<ForwardedHeadersOptions>: the web host always registers one of those.
        if (app.Services.GetService<ForwardedHeadersRegistered>() is not null)
        {
            app.UseForwardedHeaders();
        }

        app.UseMiddleware<GlobalExceptionMiddleware>();

        // After the exception handler, before everything that can answer with a bare status (the
        // limiter, the bearer challenge, routing), so those get a Problem Details body too.
        if (app.Services.GetService<IProblemDetailsService>() is not null)
        {
            app.UseStatusCodePages();
        }

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

        if (app.Services.GetService<UserRateLimiter>() is not null)
        {
            app.UseMiddleware<UserRateLimitMiddleware>();
        }

        app.UseAuthorization();
        return app;
    }

    private sealed class ForwardedHeadersRegistered;
}
