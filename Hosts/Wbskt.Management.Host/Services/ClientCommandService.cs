using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

public sealed class ClientCommandService : IClientCommandService
{
    /// <summary>The furthest ahead a command's expiresAt may be.</summary>
    private static readonly TimeSpan MaxCommandLifetime = TimeSpan.FromHours(24);

    /// <summary>How long a command or ping waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan ActionPublishTimeout = TimeSpan.FromSeconds(5);

    private const int MaxPayloadLength = 32 * 1024;

    public static readonly Error BrokerUnavailable = Error.Unavailable("EVENT_BUS_UNAVAILABLE", "The device could not be reached right now. Try again shortly.");

    private readonly IClientQueryService _clientService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientCommandService> _logger;

    public ClientCommandService(IClientQueryService clientService, IEventBus eventBus, ILogger<ClientCommandService> logger)
    {
        _clientService = clientService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<ClientCommandResponse>> SendAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return Result<ClientCommandResponse>.Failure(invalid);
        }

        // Ownership has to be settled here: the command goes out over the bus and the socket host
        // routes it by ClientRefId alone, so nothing downstream would notice a client from another
        // workspace. Presence too: commands are delivered live or not at all, so an offline device
        // is a 409 DEVICE_OFFLINE now rather than a 202 for a command that goes nowhere.
        var targetResult = await _clientService.ResolveCommandTargetAsync(workspaceId, clientRefId, cancellationToken);
        if (targetResult.IsFailure)
        {
            return Result<ClientCommandResponse>.Failure(targetResult.Error);
        }

        var target = targetResult.Value;
        var commandId = Guid.NewGuid();
        var command = new ClientCommandEvent(clientRefId, target.ClientId, workspaceId, request.Type, request.Payload, commandId,
            TargetHostId: target.HostId, ExpiresAtUtc: request.ExpiresAt?.UtcDateTime);
        if (!await TryPublishActionAsync(command, cancellationToken))
        {
            return Result<ClientCommandResponse>.Failure(BrokerUnavailable);
        }

        _logger.LogDebug("Published client command '{CommandId}' for ClientRefId: '{ClientRefId}'", commandId, clientRefId);
        return Result<ClientCommandResponse>.Success(new ClientCommandResponse(commandId));
    }

    public async Task<Result> PingAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        // Same reasoning as SendAsync: the ping is dispatched by ClientRefId, so the workspace it
        // belongs to is only ever checked here.
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return Result.Failure(clientResult.Error);
        }

        if (!await TryPublishActionAsync(new ClientPingEvent(clientRefId, clientResult.Value, workspaceId, DateTime.UtcNow), cancellationToken))
        {
            return Result.Failure(BrokerUnavailable);
        }

        _logger.LogDebug("Published client ping for ClientRefId: '{ClientRefId}'", clientRefId);
        return Result.Success();
    }

    private static Error? Validate(ClientCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 100)
        {
            return Error.Validation("COMMAND_TYPE_INVALID", "Command type must be 1-100 characters.");
        }

        if (ReservedMessageTypes.IsReserved(request.Type))
        {
            return Error.Validation("COMMAND_TYPE_RESERVED", "Command type is reserved for the platform protocol.");
        }

        if (request.Payload is { Length: > MaxPayloadLength })
        {
            return Error.Validation("COMMAND_PAYLOAD_TOO_LARGE", "Command payload is limited to 32768 characters.");
        }

        if (request.ExpiresAt is { } expiresAt
            && (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.Add(MaxCommandLifetime)))
        {
            return Error.Validation("COMMAND_EXPIRY_INVALID", "expiresAt must be in the future and at most 24 hours away.");
        }

        return null;
    }

    /// <summary>
    /// Publishes an event that is itself the action on the real bus. A queued command would be
    /// reported as sent when it might never go out, so a broker that fails or does not answer in time
    /// is reported as such, rather than as a 500 or a hung request.
    /// </summary>
    private async Task<bool> TryPublishActionAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ActionPublishTimeout);

        try
        {
            await _eventBus.PublishAsync(@event, timeout.Token);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Publishing {EventType} failed; the event bus is unavailable. {Message}", typeof(TEvent).Name, ex.Message);
            return false;
        }
    }
}
