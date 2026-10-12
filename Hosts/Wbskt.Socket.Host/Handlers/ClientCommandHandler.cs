using System.Net.WebSockets;
using MassTransit;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

public sealed class ClientCommandHandler : IConsumer<ClientCommandEvent>
{
    internal const string ExpiredReason = "The command expired before it could be delivered.";
    internal const string SendFailedReason = "The command could not be sent to the device.";

    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ClientCommandHandler> _logger;
    private readonly IEventBus _eventBus;
    private readonly BusInstanceId _hostId;
    private readonly TimeProvider _timeProvider;

    public ClientCommandHandler(IConnectionManager connectionManager, ILogger<ClientCommandHandler> logger, IEventBus eventBus, BusInstanceId hostId, TimeProvider? timeProvider = null)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _eventBus = eventBus;
        _hostId = hostId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task Consume(ConsumeContext<ClientCommandEvent> context)
    {
        var command = context.Message;

        // Every socket host receives every command, but only the one the sender saw holding the
        // device acts on it. A device that moved in between gets one answer, "not connected", rather
        // than a delivery from its new host and a failure from its old one.
        if (command.TargetHostId != null && command.TargetHostId != _hostId.Value)
        {
            return;
        }

        var connection = _connectionManager.GetConnection(command.ClientRefId);
        if (connection?.Socket.State != WebSocketState.Open)
        {
            if (command.TargetHostId != null)
            {
                _logger.LogInformation("Command {CommandId} for {ClientRefId} not delivered: the connection has gone.", command.CommandId, command.ClientRefId);
                await PublishFailedAsync(command, ClientCommandFailedEvent.NotConnectedReason, context.CancellationToken);
            }
            else
            {
                _logger.LogDebug("Received command for {ClientRefId} but it is not connected or open.", command.ClientRefId);
            }

            return;
        }

        if (command.ExpiresAtUtc is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            _logger.LogInformation("Command {CommandId} for {ClientRefId} expired at {ExpiresAt} before delivery.", command.CommandId, command.ClientRefId, expiresAt);
            await PublishFailedAsync(command, ExpiredReason, context.CancellationToken);
            return;
        }

        try
        {
            await HandleCommandAsync(connection, command, context.CancellationToken);
        }
        catch (Exception ex)
        {
            // The exception's text is internal ("Cannot access a disposed object"); the event log and
            // the console get a fixed reason, and the detail stays in this host's log.
            _logger.LogWarning(ex, "Command {CommandId} for {ClientRefId} could not be sent.", command.CommandId, command.ClientRefId);
            await PublishFailedAsync(command, SendFailedReason, context.CancellationToken);
            return;
        }

        // The device has the command now. Failing here would retry the message (ConsumerRetry.Default)
        // and send the command a second time, which a device acting on it (a relay toggle) cannot tell
        // from a new one. Losing the delivery record is the smaller harm: the device's ack still arrives.
        try
        {
            await _eventBus.PublishAsync(new ClientCommandDeliveredEvent(command.ClientRefId, command.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId), context.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Command {CommandId} for {ClientRefId} was sent, but its delivery could not be announced.", command.CommandId, command.ClientRefId);
        }
    }

    private Task PublishFailedAsync(ClientCommandEvent command, string reason, CancellationToken ct)
    {
        return _eventBus.PublishAsync(new ClientCommandFailedEvent(command.ClientRefId, command.ClientId, command.WorkspaceId, command.Type, reason, command.CommandId), ct);
    }

    private async Task HandleCommandAsync(ClientConnection connection, ClientCommandEvent command, CancellationToken ct)
    {
        _logger.LogInformation("Sending command {Type} to client {ClientRefId}.", command.Type, command.ClientRefId);

        // The SDK auto-acks frames carrying a commandId with sys.ack, and refuses (still acking, with
        // a reason) one that arrives past expiresAt.
        var message = new SocketMessage(command.Type, command.Payload, command.CommandId?.ToString())
        {
            ExpiresAt = command.ExpiresAtUtc is { } expiresAt ? new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)) : null
        };
        await connection.SendAsync(message, ct);
    }
}
