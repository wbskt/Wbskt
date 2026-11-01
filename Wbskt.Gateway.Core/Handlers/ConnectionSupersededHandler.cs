using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Handlers;

// With per-instance fan-out (WP2.1), every gateway instance observes every
// ClientConnectedEvent, including ones published by other instances. If another instance just
// accepted a connection for a client we're still holding locally with an older connection, ours
// is the stale duplicate (e.g. the client's previous host never noticed the network drop) -
// close it. A superseded connection publishes no ClientDisconnectedEvent: the device is not offline,
// and the new host already owns ConnectedHostId (see Client_UpdatePresence.sql).
//
// EstablishedAtUtc/CreatedAtUtc are stamped by two different containers' clocks with no sync
// guarantee, so a strict ">=" compare is unsafe: it can fail *symmetrically* under skew (each
// host's clock reads its own connection as later), leaving a stale duplicate open forever on
// both sides with nothing left to ever reconcile it. Superseding a connection that turns out to
// have actually been the newer one is comparatively cheap - the client just reconnects - so ties
// are resolved in favor of closing. ClockSkewTolerance only protects the case where our
// connection is unambiguously newer (by more than any realistic clock drift or event-delivery
// delay), e.g. a delayed event about an old connection on another host arriving after we've
// already re-accepted this client locally.
public sealed class ConnectionSupersededHandler : IConsumer<ClientConnectedEvent>
{
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromSeconds(5);

    private readonly IConnectionManager _connectionManager;
    private readonly BusInstanceId _busInstanceId;
    private readonly ILogger<ConnectionSupersededHandler> _logger;

    public ConnectionSupersededHandler(
        IConnectionManager connectionManager,
        BusInstanceId busInstanceId,
        ILogger<ConnectionSupersededHandler> logger)
    {
        _connectionManager = connectionManager;
        _busInstanceId = busInstanceId;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        var message = context.Message;
        if (message.HostId == _busInstanceId.Value)
        {
            // Our own connect event, echoed back to us; nothing to supersede.
            return;
        }

        var existing = _connectionManager.GetConnection(message.ClientRefId);
        if (existing is not { IsOpen: true })
        {
            return;
        }

        if (existing.EstablishedAtUtc > message.CreatedAtUtc + ClockSkewTolerance)
        {
            return;
        }

        _logger.LogInformation(
            "Client {ClientRefId} connected on host {HostId}; closing our superseded connection.",
            message.ClientRefId, message.HostId);

        await existing.RequestCloseAsync(
            DisconnectReason.Superseded,
            "Superseded by a newer connection on another host.");
    }
}
