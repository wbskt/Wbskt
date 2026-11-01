using System.Diagnostics.Metrics;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Telemetry;

/// <summary>
/// Device connections on this gateway instance, tagged with the transport that carries them. No
/// client or workspace tag: one series per device is unbounded cardinality, and who disconnected is
/// already in the ClientDisconnectedEvent stream.
/// </summary>
/// <remarks>
/// The instruments keep the <c>wbskt_socket_*</c> names they had in the socket host, so the alert
/// rules and dashboards built on them carry on unchanged.
/// </remarks>
public sealed class GatewayMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Gateway";

    private readonly Meter _meter = new(MeterName);
    private readonly KeyValuePair<string, object?> _transport;
    private readonly UpDownCounter<long> _open;
    private readonly Counter<long> _connects;
    private readonly Counter<long> _disconnects;
    private readonly Counter<long> _dropped;

    public GatewayMetrics(GatewayTransport transport)
    {
        _transport = new KeyValuePair<string, object?>("transport", transport.Name);
        _open = _meter.CreateUpDownCounter<long>("wbskt_socket_connections_open");
        _connects = _meter.CreateCounter<long>("wbskt_socket_connects_total");
        _disconnects = _meter.CreateCounter<long>("wbskt_socket_disconnects_total");
        _dropped = _meter.CreateCounter<long>("wbskt_socket_messages_dropped_total");
    }

    public void Connected()
    {
        _open.Add(1, _transport);
        _connects.Add(1, _transport);
    }

    public void Disconnected(DisconnectReason reason)
    {
        _open.Add(-1, _transport);
        _disconnects.Add(1, _transport, new KeyValuePair<string, object?>("reason", reason.Tag()));
    }

    /// <summary>A device message was not passed on: over budget, or not a valid envelope.</summary>
    public void Dropped(string reason)
    {
        _dropped.Add(1, _transport, new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}
