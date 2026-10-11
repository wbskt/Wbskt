using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Socket.Host.Services.Inbound;

/// <summary>
/// <c>sys.ack</c>: the SDK's transport-level answer to a command carrying a commandId. An ack with a
/// refusal (the command arrived past its expiresAt) is reported as a failed command, so the sender
/// never sees a command that was delivered and silently not acted on.
/// </summary>
public sealed class AckHandler : IInboundMessageHandler
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<AckHandler> _logger;

    public AckHandler(IEventBus eventBus, ILogger<AckHandler> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public string Type => MessageTypes.Ack;

    public async Task HandleAsync(InboundContext context, SocketMessage message, CancellationToken cancellationToken)
    {
        var connection = context.Connection;
        if (!TryGetCommandId(message.Payload, out var commandId))
        {
            _logger.LogDebug("Received sys.ack from {ClientRefId} without a command id.", context.ClientRefId);
            return;
        }

        if (CommandRefusal.TryRead(message.Payload, out var refusedType, out var reason))
        {
            await _eventBus.PublishAsync(new ClientCommandFailedEvent(context.ClientRefId, connection.ClientId, connection.WorkspaceId, refusedType, reason, commandId), cancellationToken);
            return;
        }

        await _eventBus.PublishAsync(new ClientCommandAckedEvent(context.ClientRefId, connection.ClientId, connection.WorkspaceId, commandId), cancellationToken);
    }

    private static bool TryGetCommandId(object payload, out Guid commandId)
    {
        commandId = Guid.Empty;
        return payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("commandId", out var id)
            && id.ValueKind == JsonValueKind.String
            && id.TryGetGuid(out commandId);
    }
}
