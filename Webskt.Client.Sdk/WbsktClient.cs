using Webskt.Client.Sdk.Internal;
using Webskt.Client.Sdk.Models;

namespace Webskt.Client.Sdk;

public sealed class WbsktClient : IWbsktClient
{
    private readonly ClientConfig _config;
    private readonly IClientStorage _storage;
    private readonly AuthClient _auth;
    private readonly SocketClient _socket;

    public event Action<string, object?>? OnCommandReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public WbsktClient(ClientConfig config, IClientStorage storage)
    {
        _config = config;
        _storage = storage;
        _auth = new AuthClient(config);
        _socket = new SocketClient(config.BaseSocketUrl);

        // Forward internal events to public surface
        _socket.OnMessageReceived += (action, payload) => OnCommandReceived?.Invoke(action, payload);
        _socket.OnConnected += () => OnConnected?.Invoke();
        _socket.OnDisconnected += () => OnDisconnected?.Invoke();
    }

    public async Task StartAsync()
    {
        // 1. Resolve Credentials (Load or Register)
        var (refId, secret) = await ResolveCredentialsAsync();

        // 2. Obtain JWT
        var token = await _auth.LoginAsync(refId, secret);

        // 3. Connect to real-time gateway
        await _socket.ConnectAsync(token);
    }

    public async Task SendTelemetryAsync(string type, object payload)
    {
        await _socket.SendAsync(new
        {
            type = "telemetry",
            payload = new { subType = type, data = payload }
        });
    }

    private async Task<(Guid RefId, string Secret)> ResolveCredentialsAsync()
    {
        var (savedId, savedSecret) = await _storage.LoadCredentialsAsync();

        if (savedId.HasValue && !string.IsNullOrEmpty(savedSecret))
        {
            return (savedId.Value, savedSecret);
        }

        if (string.IsNullOrEmpty(_config.PolicyPin))
        {
            throw new InvalidOperationException("No saved credentials and no Policy PIN provided.");
        }

        var (newId, newSecret) = await _auth.RegisterAsync();
        await _storage.SaveCredentialsAsync(newId, newSecret);
        
        return (newId, newSecret);
    }

    public async ValueTask DisposeAsync()
    {
        _auth.Dispose();
        await _socket.DisposeAsync();
    }
}