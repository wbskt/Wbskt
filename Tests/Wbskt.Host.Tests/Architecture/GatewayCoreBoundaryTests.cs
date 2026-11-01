using Wbskt.Gateway.Core;

namespace Wbskt.Host.Tests.Architecture;

/// <summary>
/// Gateway.Core is shared by every device gateway, so it must not lean on any one transport. It holds
/// a device through IDeviceConnection; the WebSocket, the HTTP request and their types stay in the host.
/// </summary>
public sealed class GatewayCoreBoundaryTests
{
    // A compiled assembly references only what its code uses, so this catches a type used inside a
    // method body too, not just one in a signature. MVC is not listed: it reaches Core through
    // Wbskt.Infrastructure's shared types, and carries no transport.
    private static readonly string[] TransportAssemblyPrefixes =
    [
        "System.Net.WebSockets",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.WebSockets",
        "Microsoft.AspNetCore.Server",
    ];

    [Fact]
    public void Gateway_core_references_no_transport_assembly()
    {
        var referenced = typeof(GatewayCoreExtensions).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        var transport = referenced.Where(name => TransportAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).ToArray();

        Assert.Empty(transport);
    }

    [Fact]
    public void Gateway_core_is_referenced_by_the_websocket_gateway()
    {
        // Pins the check above to the assembly the host actually runs.
        var host = typeof(global::Wbskt.Gateway.WebSocket.Host.Program).Assembly;

        Assert.Contains(host.GetReferencedAssemblies(), a => a.Name == typeof(GatewayCoreExtensions).Assembly.GetName().Name);
    }
}
