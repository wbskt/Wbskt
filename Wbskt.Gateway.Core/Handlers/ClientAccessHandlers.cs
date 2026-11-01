using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.Handlers;

/// <summary>A deleted client: refuse every token it holds and close its connection.</summary>
public sealed class ClientDeletedHandler : IConsumer<ClientDeletedEvent>
{
    private readonly IClientAccessGate _gate;
    private readonly ILogger<ClientDeletedHandler> _logger;

    public ClientDeletedHandler(IClientAccessGate gate, ILogger<ClientDeletedHandler> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ClientDeletedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Client {ClientRefId} was deleted; closing any live connection.", message.ClientRefId);
        return _gate.RevokeAsync(message.ClientRefId, "Client deleted.", context.CancellationToken);
    }
}

/// <summary>
/// A rotated secret: refuse tokens issued before the rotation and close the connection, which was
/// opened with one of them. The device reconnects once it signs in with the new secret.
/// </summary>
public sealed class ClientSecretRotatedHandler : IConsumer<ClientSecretRotatedEvent>
{
    private readonly IClientAccessGate _gate;
    private readonly ILogger<ClientSecretRotatedHandler> _logger;

    public ClientSecretRotatedHandler(IClientAccessGate gate, ILogger<ClientSecretRotatedHandler> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ClientSecretRotatedEvent> context)
    {
        var message = context.Message;
        _logger.LogInformation("Client {ClientRefId} secret rotated; closing any live connection.", message.ClientRefId);
        return _gate.RevokeIssuedBeforeAsync(message.ClientRefId, message.RotatedAt, "Client secret rotated.", context.CancellationToken);
    }
}

/// <summary>
/// A status change: approved again, the device may sign in afresh; any other status refuses every
/// token it holds and closes its connection.
/// </summary>
public sealed class ClientStatusChangedHandler : IConsumer<ClientStatusChangedEvent>
{
    private readonly IClientAccessGate _gate;
    private readonly ILogger<ClientStatusChangedHandler> _logger;

    public ClientStatusChangedHandler(IClientAccessGate gate, ILogger<ClientStatusChangedHandler> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<ClientStatusChangedEvent> context)
    {
        var message = context.Message;

        if (message.HasAccess)
        {
            // The event carries the moment the management host recorded in Redis, so both agree.
            _gate.Reinstate(message.ClientRefId, message.CreatedAtUtc);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Client {ClientRefId} status changed to {Status}; closing any live connection.", message.ClientRefId, message.Status);
        return _gate.RevokeAsync(message.ClientRefId, "Client access revoked.", context.CancellationToken);
    }
}
