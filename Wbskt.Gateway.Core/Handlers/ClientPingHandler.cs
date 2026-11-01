using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Handlers;

public sealed class ClientPingHandler : IConsumer<ClientPingEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<ClientPingHandler> _logger;

    public ClientPingHandler(IConnectionManager connectionManager, ILogger<ClientPingHandler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientPingEvent> context)
    {
        var connection = _connectionManager.GetConnection(context.Message.ClientRefId);
        if (connection is not { IsOpen: true })
        {
            _logger.LogDebug("Received ping for {ClientRefId} but it is not connected or open.", context.Message.ClientRefId);
            return;
        }

        // Timed from the send (not the event's PingTime) so bus latency stays out of the RTT.
        await connection.SendPingAsync(context.CancellationToken);
        _logger.LogDebug("Sent sys.ping to client {ClientRefId}.", context.Message.ClientRefId);
    }
}
