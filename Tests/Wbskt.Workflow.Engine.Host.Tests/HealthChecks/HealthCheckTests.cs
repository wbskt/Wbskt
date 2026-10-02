using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wbskt.Infrastructure.HealthChecks;
using Wbskt.Workflow.Engine;
using Wbskt.Workflow.Engine.Host.HealthChecks;

namespace Wbskt.Workflow.Engine.Host.Tests.HealthChecks;

public sealed class LeadershipHealthCheckTests
{
    [Fact]
    public async Task Standby_is_not_ready()
    {
        var state = new LeadershipState();

        HealthCheckResult result = await Check(state);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>
    /// The window that matters: this instance has won the lease but EngineLeadershipCoordinator has
    /// not finished starting the bus. Routing inbound triggers here during that gap would hand them
    /// to an instance that cannot yet consume them.
    /// </summary>
    [Fact]
    public async Task Leader_whose_bus_has_not_started_is_not_ready()
    {
        var state = new LeadershipState();
        state.SetLeader(DateTime.UtcNow.AddMinutes(2));

        HealthCheckResult result = await Check(state);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Leader_with_a_started_bus_is_ready()
    {
        var state = new LeadershipState();
        state.SetLeader(DateTime.UtcNow.AddMinutes(2));
        state.SetBusStarted();

        HealthCheckResult result = await Check(state);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// <summary>Losing the lease has to take the instance back out of rotation, not just stop new work.</summary>
    [Fact]
    public async Task Demoted_leader_stops_being_ready()
    {
        var state = new LeadershipState();
        state.SetLeader(DateTime.UtcNow.AddMinutes(2));
        state.SetBusStarted();
        state.SetStandby();

        HealthCheckResult result = await Check(state);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static Task<HealthCheckResult> Check(LeadershipState state) =>
        new LeadershipHealthCheck(state).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
}

public sealed class SqlServerHealthCheckTests
{
    [Fact]
    public void Missing_connection_string_fails_at_construction()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(
            () => new SqlServerHealthCheck(configuration, "DefaultConnection"));
    }

    /// <summary>
    /// An unreachable server must report unhealthy within the probe's own timeout rather than
    /// throwing or hanging. A readiness endpoint that never answers is worse than one that fails:
    /// the caller waits instead of routing elsewhere.
    /// </summary>
    [Fact]
    public async Task Unreachable_server_reports_unhealthy_without_throwing()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Reserved-for-documentation address, so nothing local can answer it.
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=192.0.2.1,1433;Database=Wbskt;User Id=sa;Password=irrelevant;TrustServerCertificate=True;"
            })
            .Build();

        var check = new SqlServerHealthCheck(configuration, "DefaultConnection", timeoutSeconds: 2);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}

/// <summary>
/// The readiness endpoint as mapped, served over a real socket. Only checks carrying
/// <see cref="HealthCheckExtensions.ReadyTag"/> may gate it: readiness drives Traefik's routing and
/// deploy.sh, so a check registered for any other reason must not be able to take an instance out.
/// </summary>
public sealed class ReadinessEndpointTests
{
    [Fact]
    public async Task An_untagged_failing_check_does_not_fail_readiness()
    {
        await using WebApplication app = await StartAsync(checks => checks
            .AddCheck("diagnostic", () => HealthCheckResult.Unhealthy())
            .AddCheck("dependency", () => HealthCheckResult.Healthy(), tags: [HealthCheckExtensions.ReadyTag]));

        HttpResponseMessage response = await GetAsync(app, HealthCheckExtensions.ReadinessPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The control: the filter must not have switched the endpoint off altogether.</summary>
    [Fact]
    public async Task A_ready_tagged_failing_check_fails_readiness()
    {
        await using WebApplication app = await StartAsync(checks => checks
            .AddCheck("dependency", () => HealthCheckResult.Unhealthy(), tags: [HealthCheckExtensions.ReadyTag]));

        HttpResponseMessage response = await GetAsync(app, HealthCheckExtensions.ReadinessPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(Action<IHealthChecksBuilder> configure)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configure(builder.Services.AddHealthChecks());

        WebApplication app = builder.Build();
        app.MapWbsktHealthChecks();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        return await client.GetAsync(path);
    }
}
