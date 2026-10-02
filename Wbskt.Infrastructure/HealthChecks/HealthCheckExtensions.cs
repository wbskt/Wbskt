using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wbskt.Infrastructure.HealthChecks;

/// <summary>
/// The two health endpoints every host exposes, and what each one is for.
///
/// <c>/healthz</c> is liveness: the process is up and serving. It probes nothing, deliberately —
/// restarting a host does not repair a database that is down, so a dependency failure must never
/// be able to trigger a restart loop.
///
/// <c>/healthz/ready</c> is readiness: this instance can actually serve requests. It runs the
/// registered checks and answers 503 when any is unhealthy. This is the signal the compose
/// healthcheck consumes, which means it also drives Traefik's routing table and
/// <c>deploy.sh</c>'s health wait — an instance that cannot reach its dependencies stops receiving
/// traffic and a deploy that broke them fails loudly instead of reporting success.
/// </summary>
public static class HealthCheckExtensions
{
    public const string LivenessPath = "/healthz";
    public const string ReadinessPath = "/healthz/ready";

    /// <summary>
    /// The tag a check needs to gate readiness. MassTransit's bus check carries it by default; every
    /// check registered here must state it explicitly, because an untagged one is not consulted.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers a <see cref="SqlServerHealthCheck"/> against a named connection string.
    /// </summary>
    public static IHealthChecksBuilder AddSqlServerCheck(
        this IHealthChecksBuilder builder,
        string connectionStringName,
        string? name = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name ?? $"sql:{connectionStringName}",
            provider => new SqlServerHealthCheck(
                provider.GetRequiredService<IConfiguration>(), connectionStringName),
            HealthStatus.Unhealthy,
            tags: [ReadyTag]));
    }

    /// <summary>Maps both endpoints. Anonymous — every host default-denies, and neither can require a token.</summary>
    public static void MapWbsktHealthChecks(this WebApplication app)
    {
        app.MapGet(LivenessPath, () => Results.Ok()).AllowAnonymous();
        // Filtered on the tag, so a check added for some other purpose (a diagnostic, a degraded-mode
        // signal) cannot silently pull an instance out of Traefik's rotation or fail deploy.sh.
        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag)
        }).AllowAnonymous();
    }
}
