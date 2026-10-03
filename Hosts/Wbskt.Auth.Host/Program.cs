using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Extensions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Email;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.Telemetry;
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
        
        builder.AddSharedConfiguration("serilog.json", "connectionstrings.json", "rabbitmq.json", "email.json");

        builder.Host.UseSerilog(builder.CreateSerilog());

        // Add services to the container.
        builder.Services.AddSingleton<AuthMetrics>();
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        // Signs user tokens and is the only host that can; validates them with its own keys.
        builder.Services.AddWbsktTokenIssuer(JwtIssuers.Auth);
        builder.Services.AddWbsktJwtTrust(JwtIssuers.Auth, JwtAudiences.Api);
        builder.Services.AddAccessTokenRevocation(builder.Configuration);
        builder.Services.AddScoped<IAuthProvider, SqlAuthProvider>();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IManagementService, ManagementService>();
        builder.Services.AddScoped<IWorkspaceProvider, WorkspaceProvider>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddScoped<ICredentialRetentionProvider, CredentialRetentionProvider>();
        builder.Services.AddHostedService<CredentialRetentionService>();
        builder.Services.AddHttpContextAccessor();

        // Mail. Both option types bind from Auth:Email - the SMTP half describes the relay, the
        // AuthEmailOptions half describes what the messages say and where their links point. The
        // relay is this host's own: sharing WorkflowEngine:Email would mean a workflow author's
        // misconfiguration could take password resets down with it.
        builder.Services.AddSingleton(Options.Create(builder.Configuration.GetSection("Auth:Email").Get<EmailOptions>() ?? new EmailOptions()));
        builder.Services.AddSingleton(Options.Create(builder.Configuration.GetSection("Auth:Email").Get<AuthEmailOptions>() ?? new AuthEmailOptions()));
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddSingleton<OutboundMailQueue>();
        builder.Services.AddSingleton<IAuthMailer, QueuedAuthMailer>();
        builder.Services.AddHostedService<OutboundMailDispatcher>();

        builder.AddWbsktTelemetry(AuthMetrics.MeterName);

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkspaceProvider>>(ReferenceType.Workspace);

        // Event Bus
        builder.Services.AddRabbitMqEventBus(builder.Configuration);

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddWbsktJwtBearer();

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
                var allowedOrigins = builder.Configuration.GetCorsAllowedOrigins();
                if (allowedOrigins is { Length: > 0 })
                {
                    policy.SetIsOriginAllowed(allowedOrigins.IsCorsOriginAllowed).AllowAnyHeader().AllowAnyMethod();
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

            // Refresh gets its own, looser bucket. Access tokens last minutes, so every signed-in
            // console refreshes several times an hour, and a shared office address would otherwise
            // spend the login budget on routine refreshes and lock its own users out. A refresh
            // costs no password hash and needs a 64-byte random token, so there is nothing for a
            // tighter limit to protect.
            options.AddPolicy(RateLimitPolicies.TokenRefresh, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = builder.Configuration.GetValue("RateLimiting:TokenRefresh:PermitLimit", 120),
                        Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimiting:TokenRefresh:WindowMinutes", 1)),
                        QueueLimit = 0
                    }));
        });

        // Readiness probes. The bus check is registered by AddMassTransit itself (masstransit-bus,
        // tagged "ready"), so only SQL needs adding here.
        builder.Services.AddHealthChecks().AddSqlServerCheck("AuthDBConnection");

        builder.Services.AddControllers();

        builder.Services.AddCustomOpenApi();

        var app = builder.Build();

        // Loud, at startup, every time. A control that can be switched off from configuration is only
        // safe if turning it off is impossible to do quietly.
        if (!app.Services.GetRequiredService<IOptions<AuthEmailOptions>>().Value.RequireVerifiedEmailForSignIn)
        {
            app.Logger.LogError(
                "Auth:Email:RequireVerifiedEmailForSignIn is false: accounts can sign in without confirming their address. " +
                "This is intended for local development and the end-to-end suite only.");
        }

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

        if (app.Configuration.IsApiDocsEnabled(app.Environment))
        {
            // API docs (on in Development, opt-in elsewhere via ApiDocs:Enabled) are exempt from the
            // default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();

            app.MapCustomScalarApiReference();
        }

        app.UseAuthentication();
        app.UseMiddleware<IdentityMiddleware>();
        app.UseAuthorization();

        // After UseForwardedHeaders so the partition key is the real client IP, not the proxy's.
        app.UseRateLimiter();

        app.MapWbsktHealthChecks();
        app.MapWbsktJwks();
        app.MapControllers();

        await app.RunAsync();
    }
}