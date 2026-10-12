using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Devices;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public sealed class ClientCommandService : IClientCommandService
{
    /// <summary>The furthest ahead a command's expiresAt may be.</summary>
    private static readonly TimeSpan MaxCommandLifetime = TimeSpan.FromHours(24);

    /// <summary>How long a ping waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan ActionPublishTimeout = TimeSpan.FromSeconds(5);

    public static readonly Error BrokerUnavailable = Error.Unavailable("EVENT_BUS_UNAVAILABLE", "The device could not be reached right now. Try again shortly.");

    private static readonly Error DeviceOffline = Error.Conflict("DEVICE_OFFLINE", "The device is offline. Commands are only delivered to connected devices.");

    private readonly IClientQueryService _clientService;
    private readonly IDeviceCommandSender _sender;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientCommandService> _logger;

    public ClientCommandService(IClientQueryService clientService, IDeviceCommandSender sender, IEventBus eventBus, ILogger<ClientCommandService> logger)
    {
        _clientService = clientService;
        _sender = sender;
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

        var commandId = Guid.NewGuid();
        var result = await _sender.SendAsync(
            new DeviceCommand(commandId, clientRefId, workspaceId, request.Type, request.Payload, request.ExpiresAt?.UtcDateTime),
            attribute: null,
            cancellationToken);

        // An offline device is a 409 now rather than a 202 for a command that goes nowhere.
        return result.Outcome switch
        {
            DeviceCommandOutcome.Sent => Result<ClientCommandResponse>.Success(new ClientCommandResponse(commandId)),
            DeviceCommandOutcome.UnknownClient => Result<ClientCommandResponse>.Failure(WorkspaceOwnership.ClientNotFound),
            DeviceCommandOutcome.Offline => Result<ClientCommandResponse>.Failure(DeviceOffline),
            DeviceCommandOutcome.BrokerUnavailable => Result<ClientCommandResponse>.Failure(BrokerUnavailable),
            DeviceCommandOutcome.Invalid => Result<ClientCommandResponse>.Failure(ToError(result.Violation!)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
        };
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
        if (DeviceMessageRules.Check(request.Type, request.Payload) is { } violation)
        {
            return ToError(violation);
        }

        if (request.ExpiresAt is { } expiresAt
            && (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.Add(MaxCommandLifetime)))
        {
            return Error.Validation("COMMAND_EXPIRY_INVALID", "expiresAt must be in the future and at most 24 hours away.");
        }

        return null;
    }

    private static Error ToError(DeviceMessageViolation violation) => violation.Rule switch
    {
        DeviceMessageRule.TypeLength => Error.Validation("COMMAND_TYPE_INVALID", violation.Message),
        DeviceMessageRule.TypeReserved => Error.Validation("COMMAND_TYPE_RESERVED", violation.Message),
        _ => Error.Validation("COMMAND_PAYLOAD_TOO_LARGE", violation.Message)
    };

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
