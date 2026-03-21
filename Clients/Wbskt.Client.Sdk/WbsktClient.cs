using Wbskt.Client.Sdk.Internal;
using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public sealed class WbsktClient : IWbsktClient
{
    private readonly ClientConfig _config;
    private readonly IClientStorage _storage;
    private readonly AuthClient _auth;
    private readonly SocketClient _socket;
    private readonly CancellationTokenSource _cts = new();
    
    private bool _shouldReconnect = true;
    private Guid? _resolvedRefId;
    private string? _resolvedSecret;
    private ClientCapabilities? _lastCapabilities;

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
        _socket.OnConnected += HandleConnected;
        _socket.OnDisconnected += HandleDisconnect;
    }

    public async Task StartAsync()
    {
        _shouldReconnect = true;
        await ConnectInternalAsync();
        
        // Start background monitor for reconnection
        _ = Task.Run(MonitorReconnectionAsync, _cts.Token);
    }

    private async Task ConnectInternalAsync()
    {
        try
        {
            // 1. Resolve Credentials (Load or Register) if not already done
            if (!_resolvedRefId.HasValue)
            {
                (_resolvedRefId, _resolvedSecret) = await ResolveCredentialsAsync();
            }

            // 2. Obtain JWT
            var token = await _auth.LoginAsync(_resolvedRefId.Value, _resolvedSecret!);

            // 3. Connect to real-time gateway
            await _socket.ConnectAsync(token);
        }
        catch (Exception)
        {
            // Reconnection monitor will handle retries
            throw;
        }
    }

    private void HandleConnected()
    {
        OnConnected?.Invoke();
        
        // Auto-broadcast capabilities on every successful connection
        if (_lastCapabilities != null)
        {
            _ = UpdateCapabilitiesAsync(_lastCapabilities);
        }
    }

    private void HandleDisconnect()
    {
        OnDisconnected?.Invoke();
    }

    private async Task MonitorReconnectionAsync()
    {
        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromMinutes(1);

        while (!_cts.Token.IsCancellationRequested)
        {
            // If we shouldn't reconnect, or we're already connected, just wait.
            if (!_shouldReconnect || _socket.IsConnected)
            {
                await Task.Delay(5000, _cts.Token);
                continue;
            }

            try
            {
                await ConnectInternalAsync();
                backoff = TimeSpan.FromSeconds(2); // Reset backoff on success
            }
            catch (Exception)
            {
                // Wait with exponential backoff if connection fails
                await Task.Delay(backoff, _cts.Token);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
            }
        }
    }

    public async Task UpdateCapabilitiesAsync(ClientCapabilities capabilities)
    {
        _lastCapabilities = capabilities;
        
        if (_socket.IsConnected)
        {
            await SendTelemetryAsync("capabilities", capabilities);
        }
    }

    public async Task SendTelemetryAsync(string type, object payload)
    {
        await _socket.SendAsync(new SocketMessage("telemetry", new 
        { 
            subType = type, 
            data = payload 
        }));
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
        _shouldReconnect = false;
        await _cts.CancelAsync();
        _auth.Dispose();
        await _socket.DisposeAsync();
        _cts.Dispose();
    }
}