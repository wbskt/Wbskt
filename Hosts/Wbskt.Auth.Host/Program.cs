using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using Serilog;
using Wbskt.Auth.Host.Extensions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host;

public static class Program
{
    private static readonly string ProgramDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.AppFolderName);

    public static async Task Main(string[] args)
    {
        Environment.SetEnvironmentVariable(Logging.LogPath, ProgramDataPath);
        Environment.SetEnvironmentVariable(Logging.LogName, typeof(Program).Namespace);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = Directory.GetCurrentDirectory()
        });
        
        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json", "jwt.json");

        builder.Host.UseSerilog(builder.CreateSerilog());

        // Add services to the container.
        builder.Services.AddSingleton<AuthMetrics>();
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddScoped<IAuthProvider, SqlAuthProvider>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IManagementService, ManagementService>();
        builder.Services.AddScoped<IWorkspaceProvider, WorkspaceProvider>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(AuthMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter());

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkspaceProvider>>(ReferenceType.Workspace);

        // Event Bus
        builder.Services.AddRabbitMqEventBus(builder.Configuration);

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]!);
        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(x =>
        {
            x.RequireHttpsMetadata = false;
            x.SaveToken = true;
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            // Default-deny, matching the management host. Every controller here is already correctly
            // attributed - AuthController's four public methods carry [AllowAnonymous] individually,
            // logout-all carries [Authorize], and the other three controllers are class-level
            // [Authorize] - so this changes no existing behaviour. It exists for the next endpoint
            // added to the host that owns tenants, roles and permissions, which would otherwise ship
            // anonymous by default.
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
                if (allowedOrigins is { Length: > 0 })
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
                }
                else if (builder.Environment.IsDevelopment())
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
            });
        });

        // Partitioned on the caller's IP, which UseForwardedHeaders has already resolved to the real
        // client address rather than Traefik's. Unauthenticated endpoints have no better partition
        // key available, so this is a brake on bulk attempts rather than per-account lockout.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Authentication, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:Authentication:PermitLimit", 10),
                        Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:Authentication:WindowMinutes", 1)),
                        QueueLimit = 0
                    }));
        });

        builder.Services.AddControllers();

        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        await app.RunStartupTasksAsync();

        // A nested `KnownProxies = { }` initializer is a no-op (it Adds nothing, the loopback
        // defaults survive) - the lists must be populated explicitly or Traefik's X-Forwarded-*
        // headers get silently ignored. Trust Traefik's own pinned addresses specifically rather
        // than clearing the lists (which would trust X-Forwarded-* from any container reachable
        // on the docker network, not just Traefik). These addresses must match the static
        // ipv4_address pinned to the traefik service on the edge/backend networks in
        // deploy/compose/docker-compose.yml.
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse("172.28.0.2"));
        forwardedHeadersOptions.KnownProxies.Add(IPAddress.Parse("172.28.1.2"));
        app.UseForwardedHeaders(forwardedHeadersOptions);

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseCors();

        if (app.Environment.IsDevelopment())
        {
            // Dev-only API docs are exempt from the default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();

            app.MapCustomScalarApiReference();
        }

        app.UseAuthentication();
        app.UseMiddleware<IdentityMiddleware>();
        app.UseAuthorization();

        // After UseForwardedHeaders so the partition key is the real client IP, not the proxy's.
        app.UseRateLimiter();

        // Auth is publicly routed (auth.<domain>), so an anonymous /metrics would be scrapeable
        // from the internet. Nothing in the stack scrapes it today; a future in-network Prometheus
        // can authenticate with a bearer token.
        app.MapPrometheusScrapingEndpoint().RequireAuthorization();
        app.MapGet("/healthz", () => Results.Ok()).AllowAnonymous();
        app.MapControllers();

        await app.RunAsync();
    }
}