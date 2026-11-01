using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.Gateway.Core.Telemetry;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary>
/// Parses a device's text message and routes it: a reserved <c>sys.*</c> type to its handler (an
/// unknown one is ignored), anything else to <see cref="DeviceMessageHandler"/>, which publishes it as
/// the device's own message.
/// </summary>
public sealed class InboundMessageDispatcher
{
    private readonly IReadOnlyDictionary<string, IInboundMessageHandler> _systemHandlers;
    private readonly DeviceMessageHandler _deviceMessages;
    private readonly GatewayMetrics _metrics;
    private readonly ILogger<InboundMessageDispatcher> _logger;

    public InboundMessageDispatcher(IEnumerable<IInboundMessageHandler> handlers, DeviceMessageHandler deviceMessages, GatewayMetrics metrics, ILogger<InboundMessageDispatcher> logger)
    {
        _systemHandlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
        _deviceMessages = deviceMessages;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task DispatchAsync(InboundContext context, string json, CancellationToken cancellationToken)
    {
        SocketMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SocketMessage>(json);
        }
        catch (JsonException ex)
        {
            _metrics.Dropped("invalid");
            _logger.LogWarning("Invalid JSON received from client {ClientRefId}: {Error}", context.ClientRefId, ex.Message);
            return;
        }

        // The type routes the message and names it in the event log and in triggers.
        if (message == null || string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > MessageTypes.MaxTypeLength)
        {
            _metrics.Dropped("invalid");
            _logger.LogDebug("Dropping a message from {ClientRefId} with a missing or over-long type.", context.ClientRefId);
            return;
        }

        if (!MessageTypes.IsSystem(message.Type))
        {
            await _deviceMessages.HandleAsync(context, message, cancellationToken);
            return;
        }

        if (_systemHandlers.TryGetValue(message.Type, out var handler))
        {
            await handler.HandleAsync(context, message, cancellationToken);
            return;
        }

        _logger.LogDebug("Ignoring unknown system message '{Type}' from {ClientRefId}.", message.Type, context.ClientRefId);
    }
}
