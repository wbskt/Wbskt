using System.Text.Json;
using Wbskt.Client.Sdk.Models;
using Wbskt.Socket.Host.Telemetry;

namespace Wbskt.Socket.Host.Services.Inbound;

/// <summary>
/// Parses a device's text message and routes it: a reserved <c>sys.*</c> type to its handler (an
/// unknown one is ignored), anything else to <see cref="DeviceMessageHandler"/>, which publishes it as
/// the device's own message.
/// </summary>
public sealed class InboundMessageDispatcher
{
    /// <summary>Reserved for the platform protocol; never reaches workflow triggers or the device-message log.</summary>
    public const string SystemPrefix = "sys.";

    // Message types longer than this are refused; the same cap CommandRefusal puts on a refused type.
    internal const int MaxTypeLength = 100;

    private readonly IReadOnlyDictionary<string, IInboundMessageHandler> _systemHandlers;
    private readonly DeviceMessageHandler _deviceMessages;
    private readonly SocketMetrics _metrics;
    private readonly ILogger<InboundMessageDispatcher> _logger;

    public InboundMessageDispatcher(IEnumerable<IInboundMessageHandler> handlers, DeviceMessageHandler deviceMessages, SocketMetrics metrics, ILogger<InboundMessageDispatcher> logger)
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
        if (message == null || string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > MaxTypeLength)
        {
            _metrics.Dropped("invalid");
            _logger.LogDebug("Dropping a message from {ClientRefId} with a missing or over-long type.", context.ClientRefId);
            return;
        }

        if (!message.Type.StartsWith(SystemPrefix, StringComparison.Ordinal))
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
