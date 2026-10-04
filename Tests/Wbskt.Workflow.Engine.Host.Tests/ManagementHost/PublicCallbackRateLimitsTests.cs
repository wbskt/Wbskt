using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Controllers.Workflow;

namespace Wbskt.Workflow.Engine.Host.Tests.ManagementHost;

/// <summary>
/// The public callback limits, driven through the same partitions the host registers. Every request
/// here comes from one address, as uplinks forwarded by a shared LoRaWAN network server do.
/// </summary>
public sealed class PublicCallbackRateLimitsTests
{
    private static readonly IPAddress Forwarder = IPAddress.Parse("52.212.223.226");
    private static readonly Guid WorkspaceA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid WorkspaceB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void Two_workspaces_behind_one_address_each_get_their_own_budget()
    {
        using var limiter = TargetLimiter(webhookPermits: 3);

        Assert.Equal(3, Acquire(limiter, Webhook(WorkspaceA, "uplink"), times: 5));
        Assert.Equal(3, Acquire(limiter, Webhook(WorkspaceB, "uplink"), times: 5));
    }

    [Fact]
    public void Each_path_of_a_workspace_has_its_own_budget()
    {
        using var limiter = TargetLimiter(webhookPermits: 2);

        Assert.Equal(2, Acquire(limiter, Webhook(WorkspaceA, "uplink"), times: 3));
        Assert.Equal(2, Acquire(limiter, Webhook(WorkspaceA, "join"), times: 3));
    }

    [Fact]
    public void Another_spelling_of_the_same_target_shares_its_budget()
    {
        using var limiter = TargetLimiter(webhookPermits: 2);

        Assert.Equal(1, Acquire(limiter, Webhook(WorkspaceA.ToString("D"), "Uplink"), times: 1));
        Assert.Equal(1, Acquire(limiter, Webhook(WorkspaceA.ToString("N"), "UPLINK"), times: 1));
        Assert.Equal(0, Acquire(limiter, Webhook(WorkspaceA.ToString("B"), "uplink"), times: 1));
    }

    [Fact]
    public void Wakes_are_limited_per_address()
    {
        using var limiter = TargetLimiter(wakePermits: 2);

        Assert.Equal(2, Acquire(limiter, Wake("token-1"), times: 2) + Acquire(limiter, Wake("token-2"), times: 2));
    }

    [Fact]
    public void One_address_has_a_ceiling_across_every_callback()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(Limits(perAddressPermits: 4).ForAddress);

        var accepted = Acquire(limiter, Webhook(WorkspaceA, "uplink"), times: 3)
                       + Acquire(limiter, Webhook(WorkspaceB, "uplink"), times: 3);

        Assert.Equal(4, accepted);
    }

    [Fact]
    public void The_ceiling_leaves_other_routes_alone()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(Limits(perAddressPermits: 1).ForAddress);
        var request = Request("/api/workspaces/x/clients");

        Assert.Equal(5, Acquire(limiter, request, times: 5));
    }

    private static PartitionedRateLimiter<HttpContext> TargetLimiter(int webhookPermits = 600, int wakePermits = 60) =>
        PartitionedRateLimiter.Create<HttpContext, string>(Limits(webhookPermits, wakePermits).ForTarget);

    private static PublicCallbackRateLimits Limits(int webhookPermits = 600, int wakePermits = 60, int perAddressPermits = 1200) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Webhook:PermitLimit"] = webhookPermits.ToString(),
            ["RateLimiting:Wake:PermitLimit"] = wakePermits.ToString(),
            ["RateLimiting:PublicCallbacks:PermitLimit"] = perAddressPermits.ToString()
        }).Build());

    private static int Acquire(PartitionedRateLimiter<HttpContext> limiter, HttpContext request, int times)
    {
        var accepted = 0;
        for (var i = 0; i < times; i++)
        {
            using var lease = limiter.AttemptAcquire(request);
            accepted += lease.IsAcquired ? 1 : 0;
        }

        return accepted;
    }

    private static HttpContext Webhook(Guid workspaceRef, string path) => Webhook(workspaceRef.ToString(), path);

    private static HttpContext Webhook(string workspaceRef, string path)
    {
        var context = Request($"/api/callbacks/webhook/{workspaceRef}/{path}");
        context.Request.RouteValues["workspaceRef"] = workspaceRef;
        context.Request.RouteValues["path"] = path;
        return context;
    }

    private static HttpContext Wake(string token)
    {
        var context = Request($"/api/callbacks/wake/{token}");
        context.Request.RouteValues["token"] = token;
        return context;
    }

    private static HttpContext Request(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = Forwarder;
        return context;
    }
}
