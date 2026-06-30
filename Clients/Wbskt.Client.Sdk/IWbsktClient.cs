using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public interface IWbsktClient : IAsyncDisposable
{
    event Action<string, object?>? OnMessageReceived;
    event Action? OnConnected;
    event Action? OnDisconnected;

    Task StartAsync();
    Task SendAsync(string type, object payload);
    Task UpdateCapabilitiesAsync(ClientCapabilities capabilities);
}
