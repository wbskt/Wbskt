using System.Diagnostics.Metrics;

namespace Wbskt.Socket.Host.Telemetry;

/// <summary>
/// Device connections on this socket instance. No client or workspace tag: one series per device is
/// unbounded cardinality, and who disconnected is already in the ClientDisconnectedEvent stream.
/// </summary>
public sealed class SocketMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Socket";

    /// <summary>The client sent a Close frame, the normal end of a session.</summary>
    public const string ClientClosed = "client_closed";

    /// <summary>The connection dropped without a Close: network loss, a killed process, a proxy timeout.</summary>
    public const string Aborted = "aborted";

    /// <summary>This instance is shutting down - a deploy or a restart, not a device problem.</summary>
    public const string ServerStopping = "server_stopping";

    /// <summary>The receive loop failed unexpectedly.</summary>
    public const string Error = "error";

    private readonly Meter _meter = new(MeterName);
    private readonly UpDownCounter<long> _open;
    private readonly Counter<long> _connects;
    private readonly Counter<long> _disconnects;

    public SocketMetrics()
    {
        _open = _meter.CreateUpDownCounter<long>("wbskt_socket_connections_open");
        _connects = _meter.CreateCounter<long>("wbskt_socket_connects_total");
        _disconnects = _meter.CreateCounter<long>("wbskt_socket_disconnects_total");
    }

    public void Connected()
    {
        _open.Add(1);
        _connects.Add(1);
    }

    public void Disconnected(string reason)
    {
        _open.Add(-1);
        _disconnects.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}
