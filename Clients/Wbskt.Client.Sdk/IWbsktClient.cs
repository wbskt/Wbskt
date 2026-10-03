using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public interface IWbsktClient : IAsyncDisposable
{
    // type, payload, commandId (non-null when the platform requests a delivery ack; the SDK acks automatically)
    event Action<string, object?, string?>? OnMessageReceived;
    event Action? OnConnected;
    event Action? OnDisconnected;

    /// <summary>Messages waiting in the offline buffer.</summary>
    int PendingMessageCount { get; }

    Task StartAsync();

    /// <summary>
    /// Sends an application message. The types "capabilities", "state.report" and anything
    /// prefixed "sys." are reserved for the platform protocol — use
    /// <see cref="UpdateCapabilitiesAsync"/> / <see cref="ReportStateAsync"/> instead.
    /// Each message carries the time it was sent. While offline, messages wait in memory
    /// (up to <see cref="ClientConfig.OfflineBufferSize"/>) and go out in order on reconnect.
    /// </summary>
    Task SendAsync(string type, object payload);
    Task UpdateCapabilitiesAsync(ClientCapabilities capabilities);
    Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch);
}
