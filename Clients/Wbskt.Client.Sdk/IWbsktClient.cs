using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public interface IWbsktClient : IAsyncDisposable
{
    // type, payload, commandId (non-null when the platform requests a delivery ack; the SDK acks automatically)
    event Action<string, object?, string?>? OnMessageReceived;
    event Action? OnConnected;
    event Action? OnDisconnected;

    Task StartAsync();

    /// <summary>
    /// Sends an application message. The types "capabilities", "state.report" and anything
    /// prefixed "sys." are reserved for the platform protocol — use
    /// <see cref="UpdateCapabilitiesAsync"/> / <see cref="ReportStateAsync"/> instead.
    /// </summary>
    Task SendAsync(string type, object payload);
    Task UpdateCapabilitiesAsync(ClientCapabilities capabilities);
    Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch);
}
