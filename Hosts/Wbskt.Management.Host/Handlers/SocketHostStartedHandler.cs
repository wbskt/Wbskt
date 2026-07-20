using MassTransit;
using Wbskt.Events.Hosting;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

// A socket-host restart means every previously-open connection is gone; clear stale presence
// flags so clients that died with the host don't show as online forever. Live clients
// reconnect within their retry window and flip back via ClientConnectedEvent.
public sealed class SocketHostStartedHandler : IConsumer<SocketHostStartedEvent>
{
    private readonly IClientProvider _clientProvider;
    private readonly ILogger<SocketHostStartedHandler> _logger;

    public SocketHostStartedHandler(IClientProvider clientProvider, ILogger<SocketHostStartedHandler> logger)
    {
        _clientProvider = clientProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SocketHostStartedEvent> context)
    {
        _logger.LogInformation("Socket host {HostId} started; resetting its stale client presence flags.", context.Message.HostId);
        await _clientProvider.ResetAllPresenceAsync(context.Message.HostId, context.CancellationToken);
    }
}
