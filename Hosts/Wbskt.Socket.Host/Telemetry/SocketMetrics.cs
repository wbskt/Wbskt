using System.Diagnostics.Metrics;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Telemetry;

/// <summary>
/// Device connections on this socket instance. No client or workspace tag: one series per device is
/// unbounded cardinality, and who disconnected is already in the ClientDisconnectedEvent stream.
/// </summary>
public sealed class SocketMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Socket";

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

    public void Disconnected(DisconnectReason reason)
    {
        _open.Add(-1);
        _disconnects.Add(1, new KeyValuePair<string, object?>("reason", reason.Tag()));
    }

    public void Dispose() => _meter.Dispose();
}
