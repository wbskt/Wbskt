using System.Diagnostics.Metrics;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Telemetry;

/// <summary>
/// Client connections on this gateway instance, tagged with the transport that carries them. No
/// client or workspace tag: one series per client is unbounded cardinality, and who disconnected is
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
    private readonly Counter<long> _recheckClosed;

    public GatewayMetrics(GatewayTransport transport)
    {
        _transport = new KeyValuePair<string, object?>("transport", transport.Name);
        _open = _meter.CreateUpDownCounter<long>("wbskt_socket_connections_open");
        _connects = _meter.CreateCounter<long>("wbskt_socket_connects_total");
        _disconnects = _meter.CreateCounter<long>("wbskt_socket_disconnects_total");
        _dropped = _meter.CreateCounter<long>("wbskt_socket_messages_dropped_total");
        _recheckClosed = _meter.CreateCounter<long>("wbskt_socket_access_recheck_closed_total");

        // A series first seen at 1 has no earlier sample, so increase() reads 0 and the alert misses the
        // first (often the only) close; start it at zero.
        _recheckClosed.Add(0, _transport);
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

    /// <summary>A client message was not passed on: over budget, or not a valid envelope.</summary>
    public void Dropped(string reason)
    {
        _dropped.Add(1, _transport, new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// The access re-check closed a connection whose revoke event this host never received: a sign that
    /// events are being lost while its broker connection drops.
    /// </summary>
    public void AccessRecheckClosed()
    {
        _recheckClosed.Add(1, _transport);
    }

    public void Dispose() => _meter.Dispose();
}
