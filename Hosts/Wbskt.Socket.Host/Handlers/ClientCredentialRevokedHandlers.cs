using System.Net.WebSockets;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.Handlers;

/// <summary>
/// A deleted client: refuse every token it holds and close its connection, as for a revocation.
/// </summary>
public sealed class ClientDeletedHandler : IConsumer<ClientDeletedEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly IRevocationCache _revocationCache;
    private readonly ILogger<ClientDeletedHandler> _logger;

    public ClientDeletedHandler(IConnectionManager connectionManager, IRevocationCache revocationCache, ILogger<ClientDeletedHandler> logger)
    {
        _connectionManager = connectionManager;
        _revocationCache = revocationCache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientDeletedEvent> context)
    {
        var message = context.Message;
        _revocationCache.Revoke(message.ClientRefId);
        _logger.LogInformation("Client {ClientRefId} was deleted; closing any live connection.", message.ClientRefId);
        await _connectionManager.RemoveConnectionAsync(message.ClientRefId, WebSocketCloseStatus.PolicyViolation, "Client deleted.", context.CancellationToken);
    }
}

/// <summary>
/// A rotated secret: refuse tokens issued before the rotation and close the connection, which was
/// opened with one of them. The device reconnects once it signs in with the new secret.
/// </summary>
public sealed class ClientSecretRotatedHandler : IConsumer<ClientSecretRotatedEvent>
{
    private readonly IConnectionManager _connectionManager;
    private readonly IRevocationCache _revocationCache;
    private readonly ILogger<ClientSecretRotatedHandler> _logger;

    public ClientSecretRotatedHandler(IConnectionManager connectionManager, IRevocationCache revocationCache, ILogger<ClientSecretRotatedHandler> logger)
    {
        _connectionManager = connectionManager;
        _revocationCache = revocationCache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ClientSecretRotatedEvent> context)
    {
        var message = context.Message;
        _revocationCache.RevokeIssuedBefore(message.ClientRefId, message.RotatedAt);
        _logger.LogInformation("Client {ClientRefId} secret rotated; closing any live connection.", message.ClientRefId);
        await _connectionManager.RemoveConnectionAsync(message.ClientRefId, WebSocketCloseStatus.PolicyViolation, "Client secret rotated.", context.CancellationToken);
    }
}
