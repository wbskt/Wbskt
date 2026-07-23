using Microsoft.Extensions.Configuration;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.Engine.Host.Tests.Runtime;

public sealed class OutboundAddressGuardTests
{
    [Theory]
    [InlineData("http://127.0.0.1/hook")]        // loopback
    [InlineData("http://10.0.0.5/hook")]         // RFC1918
    [InlineData("http://172.16.4.4/hook")]       // RFC1918
    [InlineData("http://192.168.1.10/hook")]     // RFC1918
    [InlineData("http://169.254.169.254/latest")]// link-local / cloud metadata
    [InlineData("http://100.100.0.1/hook")]      // CGNAT
    [InlineData("http://[::1]/hook")]            // IPv6 loopback
    [InlineData("http://[fd00::1]/hook")]        // IPv6 unique-local
    [InlineData("ftp://8.8.8.8/hook")]           // disallowed scheme
    public async Task Blocks_reserved_targets_and_bad_schemes(string url)
    {
        OutboundAddressDecision decision = await Guard().EvaluateAsync(new Uri(url), CancellationToken.None);
        Assert.False(decision.Allowed, decision.Reason);
    }

    [Theory]
    [InlineData("http://8.8.8.8/hook")]          // public unicast IPv4
    [InlineData("https://1.1.1.1/hook")]         // public unicast IPv4
    public async Task Allows_public_targets(string url)
    {
        OutboundAddressDecision decision = await Guard().EvaluateAsync(new Uri(url), CancellationToken.None);
        Assert.True(decision.Allowed, decision.Reason);
    }

    [Fact]
    public async Task Allows_private_targets_when_opted_in()
    {
        OutboundAddressDecision decision = await Guard(allowPrivate: true).EvaluateAsync(new Uri("http://10.0.0.5/hook"), CancellationToken.None);
        Assert.True(decision.Allowed, decision.Reason);
    }

    private static OutboundAddressGuard Guard(bool allowPrivate = false)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WorkflowEngine:Webhook:AllowPrivateNetworkTargets"] = allowPrivate ? "true" : "false"
            })
            .Build();
        return new OutboundAddressGuard(config);
    }
}
