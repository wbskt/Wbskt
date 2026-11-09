using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.Gateway.Core.Telemetry;

namespace Wbskt.Gateway.Core.Services.Inbound;

/// <summary>
/// Parses a client's text message and routes it: a reserved <c>sys.*</c> type to its handler (an
/// unknown one is ignored), anything else to <see cref="ClientMessageHandler"/>, which publishes it as
/// the client's own message.
/// </summary>
public sealed class InboundMessageDispatcher
{
    private readonly IReadOnlyDictionary<string, IInboundMessageHandler> _systemHandlers;
    private readonly ClientMessageHandler _clientMessages;
    private readonly GatewayMetrics _metrics;
    private readonly ILogger<InboundMessageDispatcher> _logger;
    private long _lastDropWarning = long.MinValue / 2;

    public InboundMessageDispatcher(IEnumerable<IInboundMessageHandler> handlers, ClientMessageHandler clientMessages, GatewayMetrics metrics, ILogger<InboundMessageDispatcher> logger)
    {
        _systemHandlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
        _clientMessages = clientMessages;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task DispatchAsync(InboundContext context, string json, CancellationToken cancellationToken)
    {
        ProtocolMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ProtocolMessage>(json);
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

        if (MessageTypes.IsSystem(message.Type) && !_systemHandlers.ContainsKey(message.Type))
        {
            _logger.LogDebug("Ignoring unknown system message '{Type}' from {ClientRefId}.", message.Type, context.ClientRefId);
            return;
        }

        // A Free account past its monthly messages: the client's own messages go through one a minute.
        // Not counted towards the budget's disconnect: a device that keeps its usual pace is slowed, not cut off.
        if (!MessageTypes.IsSystem(message.Type) && context.Allowance?.TryTake() == false)
        {
            _metrics.Dropped("allowance");
            if (context.Allowance.ConsecutiveHeldBack == 1)
            {
                _logger.LogInformation("Client {ClientRefId}'s account has used its monthly messages; holding it to one a minute.", context.ClientRefId);
            }

            return;
        }

        // Every handler publishes to the broker. A message that can't be published is dropped and
        // counted, never fatal: during a broker restart every client that sent anything would otherwise
        // be disconnected, and reconnect into the same outage.
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(ClientSessions.PublishTimeout);
        try
        {
            if (_systemHandlers.TryGetValue(message.Type, out var handler))
            {
                await handler.HandleAsync(context, message, bounded.Token);
            }
            else
            {
                await _clientMessages.HandleAsync(context, message, bounded.Token);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _metrics.Dropped("bus");
            // An outage drops every client's traffic at once; one warning every ten seconds is enough, and the metric counts the rest.
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastDropWarning);
            if (now - last >= 10_000 && Interlocked.CompareExchange(ref _lastDropWarning, now, last) == last)
            {
                _logger.LogWarning("Dropped '{Type}' from client {ClientRefId}: it could not be handed on. {Message}", message.Type, context.ClientRefId, ex.Message);
            }
            else
            {
                _logger.LogDebug("Dropped '{Type}' from client {ClientRefId}: {Message}", message.Type, context.ClientRefId, ex.Message);
            }
        }
    }
}
