using System.Net.WebSockets;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

public sealed class ClientStatusChangedHandler : IConsumer<ClientStatusChangedEvent>
{
    // dbo.Clients.Status value for Registered (see ClientStatus in the management host).
    private const byte RegisteredStatus = 1;

    private readonly IConnectionManager _connectionManager;
    private readonly IRevocationCache _revocationCache;
    private readonly ILogger<ClientStatusChangedHandler> _logger;

    public ClientStatusChangedHandler(
        IConnectionManager connectionManager,
        IRevocationCache revocationCache,
        ILogger<ClientStatusChangedHandler> logger)
    {
        _connectionManager = connectionManager;
        _revocationCache = revocationCache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientStatusChangedEvent> context)
    {
        var message = context.Message;

        if (message.Status == RegisteredStatus)
        {
            // Revived/approved clients may connect again.
            _revocationCache.Clear(message.ClientRefId);
            return;
        }

        _revocationCache.Revoke(message.ClientRefId);
        _logger.LogInformation("Client {ClientRefId} status changed to {Status}; closing any live connection.", message.ClientRefId, message.Status);
        await _connectionManager.RemoveConnectionAsync(message.ClientRefId, WebSocketCloseStatus.PolicyViolation, "Client access revoked.", context.CancellationToken);
    }
}
