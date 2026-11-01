using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;

namespace Wbskt.Gateway.Core.Services;

/// <summary>
/// One session's inbound side: what happens to each text message the transport reads. Used by the
/// session's receive loop only, so like its <see cref="MessageBudget"/> it needs no lock.
/// </summary>
public sealed class DeviceInbox
{
    private readonly InboundContext _context;
    private readonly MessageBudget _budget;
    private readonly InboundMessageDispatcher _dispatcher;
    private readonly GatewayMetrics _metrics;
    private readonly ILogger<DeviceInbox> _logger;

    internal DeviceInbox(InboundContext context, MessageBudget budget, InboundMessageDispatcher dispatcher, GatewayMetrics metrics, ILogger<DeviceInbox> logger)
    {
        _context = context;
        _budget = budget;
        _dispatcher = dispatcher;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// Takes one message from the device. Returns the reason the session must end, when this message
    /// ended it (the device stayed over its budget, and its close has been asked for); null otherwise.
    /// </summary>
    public async Task<DisconnectReason?> ReceiveAsync(string text, CancellationToken cancellationToken)
    {
        var clientRefId = _context.ClientRefId;
        var connection = _context.Connection;

        // After we asked for a close, anything but the device's own close is ignored.
        if (connection.CloseRequest != null)
        {
            return null;
        }

        // Every message becomes broker traffic (event log, live feed, workflow triggers), so a
        // device over its budget loses the excess, and one that keeps on is disconnected.
        if (!_budget.TryTake())
        {
            _metrics.Dropped("throttled");
            if (_budget.ConsecutiveDropped == 1)
            {
                _logger.LogWarning("Client {ClientRefId} is sending faster than its message budget; dropping the excess.", clientRefId);
            }

            if (_budget.Exhausted)
            {
                _logger.LogWarning("Client {ClientRefId} stayed over its message budget for {Dropped} messages; closing.", clientRefId, _budget.ConsecutiveDropped);
                await connection.RequestCloseAsync(DisconnectReason.Throttled, "Too many messages.");
                return DisconnectReason.Throttled;
            }

            return null;
        }

        _logger.LogDebug("Received from {ClientRefId}: {Message}", clientRefId, text);
        await _dispatcher.DispatchAsync(_context, text, cancellationToken);
        return null;
    }
}
