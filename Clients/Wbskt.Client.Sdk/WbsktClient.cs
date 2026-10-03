using Wbskt.Client.Sdk.Internal;
using Wbskt.Client.Sdk.Models;

namespace Wbskt.Client.Sdk;

public sealed class WbsktClient : IWbsktClient
{
    private readonly ClientConfig _config;
    private readonly IClientStorage _storage;
    private readonly AuthClient _auth;
    private readonly SocketClient _socket;
    private readonly OutboundBuffer _outbound;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    
    private bool _shouldReconnect = true;
    private Guid? _resolvedRefId;
    private string? _resolvedSecret;
    private ClientCapabilities? _lastCapabilities;

    public event Action<string, object?, string?>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public int PendingMessageCount => _outbound.Count;

    public WbsktClient(ClientConfig config, IClientStorage storage)
    {
        _config = config;
        _storage = storage;
        _auth = new AuthClient(config);
        _socket = new SocketClient(config.BaseSocketUrl);
        _outbound = new OutboundBuffer(config.OfflineBufferSize);

        // Forward internal events to public surface
        _socket.OnMessageReceived += (type, payload, commandId) => OnMessageReceived?.Invoke(type, payload, commandId);
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

        // Always advertise SDK metadata (merged with any app-declared commands) on every successful connection
        _ = AnnounceCapabilitiesAsync();
    }

    private async Task AnnounceCapabilitiesAsync()
    {
        try
        {
            // Capabilities describe the connection, so they go out first and are never buffered.
            await _socket.SendAsync(new SocketMessage("capabilities", BuildEffectiveCapabilities()));
        }
        catch (Exception)
        {
            // Connection may have dropped mid-handshake; the reconnect monitor will retry and re-announce.
            return;
        }

        await FlushOutboundAsync();
    }

    private ClientCapabilities BuildEffectiveCapabilities()
    {
        // Auto-detected SDK metadata; app-supplied values win when explicitly set.
        var app = _lastCapabilities;
        var agent = app is { Agent.Length: > 0 } ? app.Agent : SdkInfo.AgentName;
        var version = app is { Version.Length: > 0 } ? app.Version : SdkInfo.Version;
        var os = app is { OS.Length: > 0 } ? app.OS : SdkInfo.Platform;
        return new ClientCapabilities(agent, version, os, app?.Capabilities ?? []);
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
            await _socket.SendAsync(new SocketMessage("capabilities", BuildEffectiveCapabilities()));
        }
    }

    public async Task SendAsync(string type, object payload)
    {
        var message = new SocketMessage(type, payload) { SentAt = DateTimeOffset.UtcNow };

        if (_outbound.Capacity <= 0)
        {
            await _socket.SendAsync(message);
            return;
        }

        // Every message goes through the buffer so a backlog from an outage is never overtaken.
        _outbound.Enqueue(message);
        await FlushOutboundAsync();
    }

    private async Task FlushOutboundAsync()
    {
        await _flushLock.WaitAsync();
        try
        {
            while (_socket.IsConnected && _outbound.TryPeek(out var next))
            {
                try
                {
                    await _socket.SendAsync(next);
                }
                catch (Exception)
                {
                    // Keep it queued; the next connection resends it.
                    return;
                }

                _outbound.RemoveIfHead(next);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async Task ReportStateAsync(IReadOnlyDictionary<string, object?> patch)
    {
        // Partial update: only the reported variables change; the platform keeps the rest.
        await SendAsync("state.report", patch);
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
        _flushLock.Dispose();
    }
}