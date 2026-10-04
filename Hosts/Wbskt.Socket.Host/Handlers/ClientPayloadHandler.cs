using System.Net.WebSockets;
using MassTransit;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

public sealed class ClientPayloadHandler : IConsumer<ClientCommandEvent>
{
    internal const string NotConnectedReason = "The device is not connected.";
    internal const string ExpiredReason = "The command expired before it could be delivered.";

    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ClientPayloadHandler> _logger;
    private readonly IEventBus _eventBus;
    private readonly BusInstanceId _hostId;
    private readonly TimeProvider _timeProvider;

    public ClientPayloadHandler(IConnectionManager connectionManager, ILogger<ClientPayloadHandler> logger, IEventBus eventBus, BusInstanceId hostId, TimeProvider? timeProvider = null)
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
        var connection = _connectionManager.GetConnection(command.ClientRefId);
        if (connection?.Socket.State != WebSocketState.Open)
        {
            // Every socket host receives every command, so a missing connection is only news on the
            // host the sender saw holding it: that one answers, the rest stay quiet.
            if (command.TargetHostId == _hostId.Value)
            {
                _logger.LogInformation("Command {CommandId} for {ClientRefId} not delivered: the connection has gone.", command.CommandId, command.ClientRefId);
                await PublishFailedAsync(command, NotConnectedReason, context.CancellationToken);
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
            await _eventBus.PublishAsync(new ClientCommandDeliveredEvent(command.ClientRefId, command.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId), context.CancellationToken);
        }
        catch (Exception ex)
        {
            await PublishFailedAsync(command, ex.Message, context.CancellationToken);
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
