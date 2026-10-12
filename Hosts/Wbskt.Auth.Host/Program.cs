using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using StackExchange.Redis;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Controllers;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Infrastructure.Configuration;
using Wbskt.Infrastructure.Email;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Infrastructure.Json;
using Wbskt.Infrastructure.Mappers;
using Wbskt.Infrastructure.Security;
using Wbskt.Infrastructure.Telemetry;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WbsktHost.CreateBuilder(typeof(Program), args, "serilog.json", "connectionstrings.json", "rabbitmq.json", "email.json");

        // Add services to the container.
        builder.Services.AddSingleton<AuthMetrics>();
        builder.Services.AddSingleton<IIdentityService, IdentityService>();
        // Signs user tokens and is the only host that can; validates them with its own keys.
        builder.Services.AddWbsktTokenIssuer(JwtIssuers.Auth);
        builder.Services.AddWbsktJwtTrust(JwtIssuers.Auth, JwtAudiences.Api);
        builder.Services.AddAccessTokenRevocation(builder.Configuration);
        builder.Services.AddScoped<IAuthProvider, SqlAuthProvider>();
        builder.Services.AddUserIdLookup(sp => new AuthUserIdLookup(sp.GetRequiredService<IAuthProvider>()));
        // AuthService publishes through the queued bus, so sign-in never waits on RabbitMQ; see
        // QueuedEventBus. Every other service here keeps the real bus.
        builder.Services.AddQueuedEventBus();
        builder.Services.AddScopedWithQueuedEvents<IAuthService, AuthService>();
        builder.Services.AddScoped<IManagementService, ManagementService>();
        builder.Services.AddScoped<IWorkspaceProvider, WorkspaceProvider>();
        builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();
        builder.Services.AddScoped<IAuditWorkspaces, AuditWorkspaces>();
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
        builder.Services.AddSingleton(Options.Create(builder.Configuration.GetSection("Auth:RefreshCookie").Get<RefreshCookieOptions>() ?? new RefreshCookieOptions()));
        builder.Services.AddSingleton<RefreshTokenCookie>();

        builder.AddWbsktTelemetry(AuthMetrics.MeterName, QueuedEventBus.MeterName);

        // Register Keyed ReferenceMappers
        builder.Services.AddKeyedScoped<IReferenceMapper, ReferenceMapper<IWorkspaceProvider>>(ReferenceType.Workspace);

        // Event Bus
        builder.Services.AddRabbitMqEventBus(builder.Configuration);

        // Invitations, member and workspace changes record who made them and from where, as the
        // management host's actions do; the queued bus stamps the same way (see ActorStampingEventBus).
        builder.Services.AddSingleton<IRequestOriginAccessor, HttpRequestOriginAccessor>();
        builder.Services.AddActorStampingEventBus();

        // Registered after the bus so it stops first, and can still send what is queued while stopping.
        builder.Services.AddHostedService<QueuedEventDispatcher>();

        // Events written to the auth database in the same transaction as the change they announce
        // (a workspace delete); also after the bus, for the same reason.
        builder.Services.AddEventOutbox("AuthDBConnection");

        // Startup Tasks
        builder.Services.AddTransient<IStartupTask, FolderInitializationStartupTask>();

        builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddWbsktJwtBearer();

        // Default-deny: the host that owns tenants, roles and permissions must not ship an endpoint
        // anonymous by accident. Sign-in and the other public calls say [AllowAnonymous].
        builder.Services.AddWbsktProblemDetails();
        builder.Services.AddDefaultDenyAuthorization();

        // With credentials, so a console on another origin can send the refresh cookie (see RefreshTokenCookie).
        builder.Services.AddWbsktCors(builder.Configuration, builder.Environment);
        builder.Services.AddWbsktForwardedHeaders(builder.Configuration);

        // Partitioned on the caller's IP, which the forwarded headers have already resolved to the real
        // client address rather than Traefik's. Unauthenticated endpoints have no better partition
        // key available, so this is a brake on bulk attempts rather than per-account lockout.
        //
        // Three buckets, so routine traffic cannot spend the credential budget: on a shared office
        // address, people signing out or following a verification link must not lock their
        // colleagues out of signing in. In memory, which is right for one replica; a second auth
        // replica needs a Redis-backed limiter, or each replica allows the full budget.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectWithProblem();

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
            options.AddPerIpPolicy(builder.Configuration, RateLimitPolicies.Authentication, "Authentication", defaultPermitLimit: 10);

            // Refresh and logout. Access tokens last minutes, so every signed-in console refreshes
            // several times an hour, and a shared office address would otherwise spend the login
            // budget on routine refreshes. Neither costs a password hash and both need a 64-byte
            // random token, so there is nothing for a tighter limit to protect.
            options.AddPerIpPolicy(builder.Configuration, RateLimitPolicies.TokenRefresh, "TokenRefresh", defaultPermitLimit: 120);

            // Verify-email and resend-verification. Resend sends mail, so it stays tight; the
            // per-address cooldown (MailCooldown) is what protects the recipient.
            options.AddPerIpPolicy(builder.Configuration, RateLimitPolicies.EmailVerification, "EmailVerification", defaultPermitLimit: 10);
        });

        // Readiness probes. The bus check is registered by AddMassTransit itself (masstransit-bus,
        // tagged "ready"), so only SQL needs adding here.
        builder.Services.AddHealthChecks().AddSqlServerCheck("AuthDBConnection");

        // Tenant and workspace actions declare their permissions; these filters resolve the scope once
        // and refuse the request before it reaches the action.
        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<TenantPermissionFilter>();
            options.Filters.Add<WorkspacePermissionFilter>();
        });
        builder.Services.AddWbsktJson();

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

        app.UseWbsktPipeline();

        if (app.Configuration.IsApiDocsEnabled(app.Environment))
        {
            // API docs (on in Development, opt-in elsewhere via ApiDocs:Enabled) are exempt from the
            // default-deny fallback policy.
            app.MapOpenApi().AllowAnonymous();

            app.MapCustomScalarApiReference();
        }

        app.MapWbsktHealthChecks();
        app.MapWbsktJwks();
        app.MapControllers();

        await app.RunAsync();
    }
}
