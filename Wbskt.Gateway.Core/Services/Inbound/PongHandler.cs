using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary><c>sys.pong</c>: the answer to our <c>sys.ping</c> (matched by its id), turned into a latency reading.</summary>
public sealed class PongHandler : IInboundMessageHandler
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<PongHandler> _logger;

    public PongHandler(IEventBus eventBus, ILogger<PongHandler> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public string Type => MessageTypes.Pong;

    public async Task HandleAsync(InboundContext context, SocketMessage message, CancellationToken cancellationToken)
    {
        var connection = context.Connection;

        // RTT is measured here, on the connection and on this host's clock, so neither bus hops nor a
        // device's echoed timestamp can change the number.
        if (!connection.TryCompletePing(TryGetPingId(message.Payload), out var sentAt))
        {
            _logger.LogDebug("Received sys.pong from {ClientRefId} without a matching ping.", context.ClientRefId);
            return;
        }

        var roundTripMs = (DateTime.UtcNow - sentAt).TotalMilliseconds;
        await _eventBus.PublishAsync(new ClientLatencyMeasuredEvent(context.ClientRefId, connection.ClientId, connection.WorkspaceId, roundTripMs), cancellationToken);
    }

    private static string? TryGetPingId(object payload)
    {
        return payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }
}
