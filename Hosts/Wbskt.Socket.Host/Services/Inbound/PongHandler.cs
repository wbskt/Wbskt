using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Socket.Host.Services.Inbound;

/// <summary><c>sys.pong</c>: the answer to our <c>sys.ping</c>, turned into a latency reading.</summary>
public sealed class PongHandler : IInboundMessageHandler
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<PongHandler> _logger;

    public PongHandler(IEventBus eventBus, ILogger<PongHandler> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public string Type => "sys.pong";

    public async Task HandleAsync(InboundContext context, SocketMessage message, CancellationToken cancellationToken)
    {
        var connection = context.Connection;

        // RTT is measured here, on the socket, so bus hops never inflate the number.
        var originalPingTime = TryGetOriginalTimestamp(message.Payload) ?? connection.LastPingSentAt;
        if (originalPingTime == null)
        {
            _logger.LogDebug("Received sys.pong from {ClientRefId} without a matching ping.", context.ClientRefId);
            return;
        }

        var roundTripMs = (DateTime.UtcNow - originalPingTime.Value).TotalMilliseconds;
        await _eventBus.PublishAsync(new ClientLatencyMeasuredEvent(context.ClientRefId, connection.ClientId, connection.WorkspaceId, roundTripMs), cancellationToken);
    }

    private static DateTime? TryGetOriginalTimestamp(object payload)
    {
        // The client echoes the server's own sys.ping timestamp back as 'originalTimestamp',
        // so the round trip is computed entirely on this host's clock.
        if (payload is JsonElement element
            && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("originalTimestamp", out var timestamp)
            && timestamp.ValueKind == JsonValueKind.String
            && timestamp.TryGetDateTime(out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }
}
