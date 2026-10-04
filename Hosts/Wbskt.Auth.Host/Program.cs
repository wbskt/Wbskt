using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Extensions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Services.Events;
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
        // AuthService publishes through the queued bus, so sign-in never waits on RabbitMQ; see
        // QueuedEventBus. Every other service here keeps the real bus.
        builder.Services.AddSingleton<QueuedEventBus>();
        builder.Services.AddScoped<IAuthService>(sp => ActivatorUtilities.CreateInstance<AuthService>(sp, sp.GetRequiredService<QueuedEventBus>()));
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
        builder.Services.AddSingleton(Options.Create(builder.Configuration.GetSection("Auth:MailCooldown").Get<MailCooldownOptions>() ?? new MailCooldownOptions()));
        builder.Services.AddSingleton(sp => new MailCooldown(
            sp.GetRequiredService<IOptions<MailCooldownOptions>>(),
            sp.GetService<IConnectionMultiplexer>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<MailCooldown>>()));
        builder.Services.AddHostedService<OutboundMailDispatcher>();

        builder.AddWbsktTelemetry(AuthMetrics.MeterName);

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkspaceProvider>>(ReferenceType.Workspace);

        // Event Bus
        builder.Services.AddRabbitMqEventBus(builder.Configuration);

        // Registered after the bus so it stops first, and can still send what is queued while stopping.
        builder.Services.AddHostedService<QueuedEventDispatcher>();

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
        //
        // Three buckets, so routine traffic cannot spend the credential budget: on a shared office
        // address, people signing out or following a verification link must not lock their
        // colleagues out of signing in. In memory, which is right for one replica; a second auth
        // replica needs a Redis-backed limiter, or each replica allows the full budget.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Across every caller, not per IP: how many password hashes run at once. See
            // HashesPasswordAttribute. Twice the cores keeps them busy without starving the rest.
            var hashingPermits = builder.Configuration.GetValue("RateLimiting:PasswordHashing:PermitLimit", Environment.ProcessorCount * 2);
            var hashingQueue = builder.Configuration.GetValue("RateLimiting:PasswordHashing:QueueLimit", 20);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                httpContext.GetEndpoint()?.Metadata.GetMetadata<HashesPasswordAttribute>() is null
                    ? RateLimitPartition.GetNoLimiter(string.Empty)
                    : RateLimitPartition.GetConcurrencyLimiter("password-hashing", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = hashingPermits,
                        QueueLimit = hashingQueue,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    }));

            // Everything that checks or sets a password, and register, which creates an account.
            AddPerIpPolicy(options, builder.Configuration, RateLimitPolicies.Authentication, "Authentication", defaultPermitLimit: 10);

            // Refresh and logout. Access tokens last minutes, so every signed-in console refreshes
            // several times an hour, and a shared office address would otherwise spend the login
            // budget on routine refreshes. Neither costs a password hash and both need a 64-byte
            // random token, so there is nothing for a tighter limit to protect.
            AddPerIpPolicy(options, builder.Configuration, RateLimitPolicies.TokenRefresh, "TokenRefresh", defaultPermitLimit: 120);

            // Verify-email and resend-verification. Resend sends mail, so it stays tight; the
            // per-address cooldown (MailCooldown) is what protects the recipient.
            AddPerIpPolicy(options, builder.Configuration, RateLimitPolicies.EmailVerification, "EmailVerification", defaultPermitLimit: 10);
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

    /// <summary>A fixed window per client IP, read from <c>RateLimiting:{section}</c>.</summary>
    private static void AddPerIpPolicy(RateLimiterOptions options, IConfiguration configuration, string policy, string section, int defaultPermitLimit)
    {
        var permitLimit = configuration.GetValue($"RateLimiting:{section}:PermitLimit", defaultPermitLimit);
        var window = TimeSpan.FromMinutes(configuration.GetValue($"RateLimiting:{section}:WindowMinutes", 1));

        options.AddPolicy(policy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0
                }));
    }
}
