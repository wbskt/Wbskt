using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Gateway.Core;
using Wbskt.Gateway.Core.Telemetry;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>
/// A gateway's bus instance id names its queues and its clients' presence rows. Two gateways on one
/// machine (start-hosts.sh, the E2E compose, a developer's RunAll) once shared the machine name, so
/// the second could not declare its queues and missed every command, revocation and supersede.
/// </summary>
public sealed class GatewayInstanceIdTests
{
    [Fact]
    public void Each_transport_has_its_own_instance_id_on_one_machine()
    {
        string webSocket = InstanceIdOf(GatewayTransport.WebSocket);
        string mqtt = InstanceIdOf(GatewayTransport.Mqtt);

        Assert.NotEqual(webSocket, mqtt);
        Assert.Equal($"{Environment.MachineName}-mqtt", mqtt);
        Assert.Equal(mqtt, InstanceIdOf(GatewayTransport.Mqtt));
    }

    [Fact]
    public void A_configured_instance_id_wins()
    {
        Assert.Equal("gateway-7", InstanceIdOf(GatewayTransport.Mqtt, new Dictionary<string, string?> { ["EventBus:InstanceId"] = "gateway-7" }));
    }

    private static string InstanceIdOf(GatewayTransport transport, Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayCore(configuration, transport);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<BusInstanceId>().Value;
    }
}
