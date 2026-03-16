using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public interface IWbsktClient : IAsyncDisposable
{
    event Action<string, object?>? OnCommandReceived;
    event Action? OnConnected;
    event Action? OnDisconnected;

    Task StartAsync();
    Task SendTelemetryAsync(string type, object payload);
    Task UpdateCapabilitiesAsync(ClientCapabilities capabilities);
}
