using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;

namespace Wbskt.Gateway.Core.Services;

/// <summary>
/// A device session's start and end, the same over every transport. A gateway host accepts the
/// connection, calls these in order around its receive loop, and hands each message it reads to the
/// session's <see cref="DeviceInbox"/>:
/// <see cref="Open"/>, <see cref="AnnounceConnectedAsync"/>, the loop, then <see cref="Release"/>,
/// its own transport close, and <see cref="AnnounceEndedAsync"/>.
/// </summary>
public sealed class DeviceSessions
{
    private readonly IConnectionManager _connections;
    private readonly IEventBus _eventBus;
    private readonly GatewayMetrics _metrics;
    private readonly InboundMessageDispatcher _dispatcher;
    private readonly MessageBudgetOptions _budgetOptions;
    private readonly ILogger<DeviceSessions> _logger;
    private readonly ILogger<DeviceInbox> _inboxLogger;
    private readonly string _hostId;

    public DeviceSessions(
        IConnectionManager connections,
        IEventBus eventBus,
        BusInstanceId busInstanceId,
        GatewayMetrics metrics,
        InboundMessageDispatcher dispatcher,
        IOptions<MessageBudgetOptions> budgetOptions,
        ILogger<DeviceSessions> logger,
        ILogger<DeviceInbox> inboxLogger)
    {
        _connections = connections;
        _eventBus = eventBus;
        _metrics = metrics;
        _dispatcher = dispatcher;
        _budgetOptions = budgetOptions.Value;
        _logger = logger;
        _inboxLogger = inboxLogger;
        _hostId = busInstanceId.Value;
    }

    /// <summary>
    /// Makes <paramref name="connection"/> the device's connection here. The newest connection wins:
    /// one this instance still holds for the device is closed as superseded.
    /// </summary>
    public void Open(Guid clientRefId, IDeviceConnection connection)
    {
        _connections.Register(clientRefId, connection);
        _logger.LogInformation("Client {ClientRefId} connected.", clientRefId);
        _metrics.Connected();
    }

    /// <summary>
    /// Tells the rest of the system the device is online. Not tied to the connection: a device that
    /// drops straight away must still be recorded as having connected, or its disconnect has nothing
    /// to pair with.
    /// </summary>
    public Task AnnounceConnectedAsync(Guid clientRefId, IDeviceConnection connection)
    {
        return _eventBus.PublishAsync(new ClientConnectedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, _hostId), CancellationToken.None);
    }

    /// <summary>A fresh inbox for the session: one message budget per connection.</summary>
    public DeviceInbox CreateInbox(Guid clientRefId, IDeviceConnection connection)
    {
        return new DeviceInbox(new InboundContext(clientRefId, connection), new MessageBudget(_budgetOptions), _dispatcher, _metrics, _inboxLogger);
    }

    /// <summary>Forgets the connection, unless the device's connection has already been replaced.</summary>
    public void Release(Guid clientRefId, IDeviceConnection connection)
    {
        _connections.Unregister(clientRefId, connection);
    }

    /// <summary>
    /// Records how the session ended and, unless it was superseded, tells the rest of the system the
    /// device went offline.
    /// </summary>
    public async Task AnnounceEndedAsync(Guid clientRefId, IDeviceConnection connection, DisconnectReason reason)
    {
        _metrics.Disconnected(reason);
        _logger.LogInformation("Client {ClientRefId} disconnected ({Reason}).", clientRefId, reason.Tag());

        // A superseded connection is not the device going offline: its newer connection, here or on
        // another instance, already announced it as connected.
        if (reason != DisconnectReason.Superseded)
        {
            var description = connection.CloseRequest?.Description ?? reason.Describe();
            await _eventBus.PublishAsync(new ClientDisconnectedEvent(clientRefId, connection.ClientId, connection.WorkspaceId, description, _hostId), CancellationToken.None);
        }
    }
}
