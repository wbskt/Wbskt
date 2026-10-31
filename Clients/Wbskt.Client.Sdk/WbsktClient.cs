using Wbskt.Protocol;
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
    private readonly ReconnectPolicy _reconnect = new();
    private readonly DeviceTokenCache _tokens;

    /// <summary>How often a connected client checks whether its socket is still open.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    
    private bool _shouldReconnect = true;
    // Set once this connection's capabilities are out; nothing else is sent before them.
    private volatile bool _announced;
    private Guid? _resolvedRefId;
    private string? _resolvedSecret;
    private ClientCapabilities? _lastCapabilities;

    public event Action<string, object?, string?>? OnMessageReceived;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public int PendingMessageCount => _outbound.Count;

    /// <summary>
    /// A device named <paramref name="deviceName"/> on the hosted service, enrolling with
    /// <paramref name="policyPin"/> the first time and keeping its credentials in a file
    /// (<see cref="FileClientStorage.ForDevice"/>) for every start after that.
    /// </summary>
    public WbsktClient(string deviceName, string policyPin)
        : this(new ClientConfig(WbsktDefaults.ApiUrl, WbsktDefaults.SocketUrl, deviceName, policyPin), FileClientStorage.ForDevice(deviceName))
    {
    }

    /// <summary>Custom URLs (self-hosted, staging) and buffering, with credentials kept in a file.</summary>
    public WbsktClient(ClientConfig config)
        : this(config, FileClientStorage.ForDevice(config.DeviceName))
    {
    }

    public WbsktClient(ClientConfig config, IClientStorage storage)
    {
        _config = config;
        _storage = storage;
        _auth = new AuthClient(config);
        _socket = new SocketClient(config.BaseSocketUrl);
        _outbound = new OutboundBuffer(config.OfflineBufferSize);
        _tokens = new DeviceTokenCache(() => _auth.LoginAsync(_resolvedRefId!.Value, _resolvedSecret!));

        // Forward internal events to public surface
        _socket.OnMessageReceived += (type, payload, commandId) => OnMessageReceived?.Invoke(type, payload, commandId);
        _socket.OnConnected += HandleConnected;
        _socket.OnDisconnected += HandleDisconnect;
    }

    /// <summary>
    /// Makes the first connection attempt and returns once it has finished, connected or not. A
    /// failed attempt is retried in the background; only <see cref="RegistrationRequiredException"/>
    /// is thrown, because no retry can fix it.
    /// </summary>
    public async Task StartAsync()
    {
        _shouldReconnect = true;
        var failures = 0;
        TimeSpan? retryIn = null;
        try
        {
            await ConnectInternalAsync();
        }
        catch (RegistrationRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            retryIn = FailureDelay(ex, failures++);
        }

        _ = Task.Run(() => MonitorReconnectionAsync(failures, retryIn), _cts.Token);
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

            // 2. Obtain JWT, reusing the last one while it is still valid
            string token;
            try
            {
                token = await _tokens.GetAsync();
            }
            catch (CredentialsRejectedException)
            {
                // Deleted, or its secret rotated: these credentials never work again, so register anew.
                await _storage.ClearCredentialsAsync();
                _resolvedRefId = null;
                _resolvedSecret = null;
                throw;
            }

            // 3. Connect to real-time gateway
            _announced = false;
            try
            {
                await _socket.ConnectAsync(token);
            }
            catch (Exception) when (_socket.TokenRejected)
            {
                _tokens.Invalidate();
                throw;
            }
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
            await _socket.SendAsync(new SocketMessage(MessageTypes.Capabilities, BuildEffectiveCapabilities()));
        }
        catch (Exception)
        {
            // Connection may have dropped mid-handshake; the reconnect monitor will retry and re-announce.
            return;
        }

        _announced = true;
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
        _announced = false;
        OnDisconnected?.Invoke();
    }

    private async Task MonitorReconnectionAsync(int failures, TimeSpan? retryIn)
    {
        if (retryIn is { } firstWait)
        {
            await Task.Delay(firstWait, _cts.Token);
        }

        while (!_cts.Token.IsCancellationRequested)
        {
            if (!_shouldReconnect || _socket.IsConnected)
            {
                failures = 0;
                await Task.Delay(PollInterval, _cts.Token);
                continue;
            }

            if (failures == 0)
            {
                // Just dropped. Every device that shared this socket host noticed at the same moment,
                // so each waits a random part of the window before its first attempt.
                await Task.Delay(_reconnect.FirstDelay(_socket.ServerRestarting), _cts.Token);
            }

            try
            {
                await ConnectInternalAsync();
                failures = 0;
            }
            catch (Exception ex)
            {
                await Task.Delay(FailureDelay(ex, failures++), _cts.Token);
            }
        }
    }

    private TimeSpan FailureDelay(Exception error, int failures) =>
        error is DeviceLoginThrottledException { RetryAfter: { } retryAfter }
            ? _reconnect.AfterThrottle(retryAfter)
            : _reconnect.RetryDelay(failures);

    public async Task UpdateCapabilitiesAsync(ClientCapabilities capabilities)
    {
        _lastCapabilities = capabilities;

        if (_socket.IsConnected)
        {
            await _socket.SendAsync(new SocketMessage(MessageTypes.Capabilities, BuildEffectiveCapabilities()));
        }
    }

    /// <exception cref="ArgumentException">
    /// The type is empty, too long, or reserved for the protocol (<c>sys.*</c>, <c>capabilities</c>, <c>state.report</c>).
    /// </exception>
    public Task SendAsync(string type, object payload)
    {
        if (DeviceMessageRules.Check(type, null) is { } violation)
        {
            throw new ArgumentException(violation.Message, nameof(type));
        }

        return EnqueueAsync(type, payload);
    }

    private async Task EnqueueAsync(string type, object payload)
    {
        var message = new SocketMessage(type, payload) { SentAt = DateTimeOffset.UtcNow };

        if (_outbound.Capacity <= 0)
        {
            if (!_announced)
            {
                throw new InvalidOperationException("Socket not connected.");
            }

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
            while (_announced && _socket.IsConnected && _outbound.TryPeek(out var next))
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
        await EnqueueAsync(MessageTypes.StateReport, patch);
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
            throw new RegistrationRequiredException();
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