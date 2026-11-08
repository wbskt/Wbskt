using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using MQTTnet.Adapter;
using MQTTnet.Diagnostics.Logger;
using MQTTnet.Formatter;
using MQTTnet.Implementations;
using MQTTnet.Server;

namespace Wbskt.Gateway.MQTT.Host.Infrastructure;

/// <summary>
/// The MQTT server's TCP listener, in place of MQTTnet's own so that every connection's bytes pass
/// through a <see cref="PacketSizeGuardStream"/>. TLS is not here: Traefik ends it in front of the
/// host, as it does for the WebSocket gateway.
/// </summary>
public sealed class GuardedTcpServerAdapter : IMqttServerAdapter
{
    private readonly int _port;
    private readonly int _maxPacketBytes;
    private readonly ConcurrentDictionary<IMqttChannelAdapter, PacketSizeGuardStream> _guards = new();
    private readonly CancellationTokenSource _stopping = new();
    private Socket? _listener;
    private Task _accepting = Task.CompletedTask;

    public GuardedTcpServerAdapter(int port, int maxPacketBytes)
    {
        _port = port;
        _maxPacketBytes = maxPacketBytes;
    }

    public Func<IMqttChannelAdapter, Task>? ClientHandler { get; set; }

    /// <summary>The port it listens on; the one asked for, or the one the system chose for port 0.</summary>
    public int Port => (_listener?.LocalEndPoint as IPEndPoint)?.Port ?? _port;

    /// <summary>The guard reading a connection, while it is open.</summary>
    public PacketSizeGuardStream? GuardOf(IMqttChannelAdapter channel) => _guards.GetValueOrDefault(channel);

    public Task StartAsync(MqttServerOptions options, IMqttNetLogger logger)
    {
        // Both families where the host has IPv6, IPv4 alone where a container has it turned off.
        var dual = Socket.OSSupportsIPv6;
        var listener = new Socket(dual ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (dual)
        {
            listener.DualMode = true;
        }

        listener.Bind(new IPEndPoint(dual ? IPAddress.IPv6Any : IPAddress.Any, _port));
        listener.Listen(options.DefaultEndpointOptions.ConnectionBacklog);
        _listener = listener;
        _accepting = AcceptAsync(listener, logger);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await _stopping.CancelAsync();
        _listener?.Dispose();
        await _accepting;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener?.Dispose();
        _stopping.Dispose();
    }

    private async Task AcceptAsync(Socket listener, IMqttNetLogger logger)
    {
        while (!_stopping.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptAsync(_stopping.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A connection reset before it was accepted; the listener itself is fine.
                continue;
            }

            _ = Task.Run(() => ServeAsync(socket, logger));
        }
    }

    private async Task ServeAsync(Socket socket, IMqttNetLogger logger)
    {
        // Notices a peer that vanished without a FIN (power or Wi-Fi lost) even when its MQTT
        // keep-alive is off, so it does not stay "online" until the OS gives up on its own.
        socket.NoDelay = true;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 10);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);

        var guard = new PacketSizeGuardStream(new NetworkStream(socket, ownsSocket: true), _maxPacketBytes);
        var channel = new MqttTcpChannel(guard, socket.RemoteEndPoint!, socket.LocalEndPoint!, null);
        using var adapter = new MqttChannelAdapter(channel, new MqttPacketFormatterAdapter(new MqttBufferWriter(4096, 65535)), logger);
        _guards[adapter] = guard;
        try
        {
            if (ClientHandler is { } handler)
            {
                await handler(adapter);
            }
        }
        catch (Exception)
        {
            // The server logs and ends the connection; a failure here is that one client's.
        }
        finally
        {
            _guards.TryRemove(adapter, out _);
            await guard.DisposeAsync();
        }
    }
}
