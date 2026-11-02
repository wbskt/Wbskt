using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Events.Abstractions;
using Wbskt.Infrastructure.Devices;
using Wbskt.Infrastructure.Events;
using Wbskt.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Management.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public sealed class ClientCommandService : IClientCommandService
{
    /// <summary>How long a ping waits for the broker before the caller is told to retry.</summary>
    internal static readonly TimeSpan ActionPublishTimeout = TimeSpan.FromSeconds(5);

    public static readonly Error BrokerUnavailable = Error.Unavailable("EVENT_BUS_UNAVAILABLE", "The device could not be reached right now. Try again shortly.");

    private static readonly Error DeviceInactive = Error.Conflict("DEVICE_INACTIVE", "The device is offline and not registered, so a command cannot wait for it.");

    private static readonly Error OtaNotSupported = Error.Conflict("OTA_NOT_SUPPORTED", "The device does not take firmware updates: its capabilities do not list 'ota'.");

    private const int MaxOtaUrlLength = 2048;
    private const int MaxOtaVersionLength = 50;

    private static readonly Error NotQueued = Error.NotFound("COMMAND_NOT_QUEUED", "The command is not waiting: it was delivered, expired or cancelled.");

    private readonly IClientQueryService _clientService;
    private readonly IDeviceCommandSender _sender;
    private readonly IQueuedCommandStore _queue;
    private readonly IEventBus _eventBus;
    private readonly IIdentityService? _identity;
    private readonly IRequestOriginAccessor? _requestOrigin;
    private readonly int _maxQueuedPerClient;
    private readonly TimeSpan _defaultExpiry;
    private readonly ILogger<ClientCommandService> _logger;
    private readonly ManagementMetrics _metrics;

    public ClientCommandService(
        IClientQueryService clientService,
        IDeviceCommandSender sender,
        IQueuedCommandStore queue,
        IEventBus eventBus,
        IOptions<CommandQueueOptions> queueOptions,
        ILogger<ClientCommandService> logger,
        ManagementMetrics metrics,
        IIdentityService? identity = null,
        IRequestOriginAccessor? requestOrigin = null)
    {
        _clientService = clientService;
        _sender = sender;
        _queue = queue;
        _eventBus = eventBus;
        _maxQueuedPerClient = queueOptions.Value.MaxPerClient;
        _defaultExpiry = queueOptions.Value.DefaultExpiry;
        _logger = logger;
        _metrics = metrics;
        _identity = identity;
        _requestOrigin = requestOrigin;
    }

    public async Task<Result<ClientCommandResponse>> SendAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync(workspaceId, clientRefId, request, cancellationToken);
        _metrics.RecordCommand(result);
        return result;
    }

    private async Task<Result<ClientCommandResponse>> SendCommandAsync(int workspaceId, Guid clientRefId, ClientCommandRequest request, CancellationToken cancellationToken)
    {
        var invalid = Validate(request);
        if (invalid is not null)
        {
            return Result<ClientCommandResponse>.Failure(invalid);
        }

        return await DispatchAsync(new DeviceCommand(Guid.NewGuid(), clientRefId, workspaceId, request.Type, request.Payload, request.ExpiresAt?.UtcDateTime), request.ExpiresAt, cancellationToken);
    }

    public async Task<Result<ClientCommandResponse>> SendOtaAsync(int workspaceId, Guid clientRefId, ClientOtaRequest request, CancellationToken cancellationToken = default)
    {
        var result = await SendOtaCommandAsync(workspaceId, clientRefId, request, cancellationToken);
        _metrics.RecordCommand(result);
        return result;
    }

    private async Task<Result<ClientCommandResponse>> SendOtaCommandAsync(int workspaceId, Guid clientRefId, ClientOtaRequest request, CancellationToken cancellationToken)
    {
        var invalid = ValidateOta(request);
        if (invalid is not null)
        {
            return Result<ClientCommandResponse>.Failure(invalid);
        }

        // What the device last reported decides; one that is offline now is judged by its last connection.
        var detail = await _clientService.GetDetailAsync(workspaceId, clientRefId, cancellationToken);
        if (detail.IsFailure)
        {
            return Result<ClientCommandResponse>.Failure(detail.Error);
        }

        if (!detail.Value.System.Contains(SystemCapabilities.Ota, StringComparer.Ordinal))
        {
            return Result<ClientCommandResponse>.Failure(OtaNotSupported);
        }

        // An update that turns up days later is a surprise, so it always expires.
        var expiresAt = request.ExpiresAt ?? DateTimeOffset.UtcNow.Add(_defaultExpiry);
        var payload = JsonSerializer.Serialize(new OtaUpdatePayload(request.Url, request.Sha256.ToLowerInvariant(), NullIfEmpty(request.Version), request.Size));
        return await DispatchAsync(
            new DeviceCommand(Guid.NewGuid(), clientRefId, workspaceId, MessageTypes.OtaUpdate, payload, expiresAt.UtcDateTime, Platform: true),
            expiresAt,
            cancellationToken);
    }

    private async Task<Result<ClientCommandResponse>> DispatchAsync(DeviceCommand command, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var commandId = command.CommandId;
        var result = await _sender.SendAsync(command, StampActor, cancellationToken);

        return result.Outcome switch
        {
            DeviceCommandOutcome.Sent => Result<ClientCommandResponse>.Success(new ClientCommandResponse(commandId, ClientCommandStatus.Sent, expiresAt)),
            DeviceCommandOutcome.Queued => Result<ClientCommandResponse>.Success(new ClientCommandResponse(commandId, ClientCommandStatus.Queued, Utc(result.ExpiresAtUtc!.Value))),
            DeviceCommandOutcome.QueueFull => Result<ClientCommandResponse>.Failure(Error.Conflict(
                "COMMAND_QUEUE_FULL",
                $"The device is offline and already has {_maxQueuedPerClient} commands waiting. Cancel some, or wait for it to come back.")),
            DeviceCommandOutcome.Inactive => Result<ClientCommandResponse>.Failure(DeviceInactive),
            DeviceCommandOutcome.UnknownClient => Result<ClientCommandResponse>.Failure(WorkspaceOwnership.ClientNotFound),
            DeviceCommandOutcome.BrokerUnavailable => Result<ClientCommandResponse>.Failure(BrokerUnavailable),
            DeviceCommandOutcome.Invalid => Result<ClientCommandResponse>.Failure(ToError(result.Violation!)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
        };
    }

    public async Task<Result<IReadOnlyList<QueuedClientCommandResponse>>> GetQueuedAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return Result<IReadOnlyList<QueuedClientCommandResponse>>.Failure(clientResult.Error);
        }

        var queued = await _queue.GetAsync(clientResult.Value, cancellationToken);
        return Result<IReadOnlyList<QueuedClientCommandResponse>>.Success(
            [.. queued.Select(c => new QueuedClientCommandResponse(c.CommandId, c.Type, c.Payload, Utc(c.QueuedAtUtc), Utc(c.ExpiresAtUtc)))]);
    }

    public async Task<Result> CancelAsync(int workspaceId, Guid clientRefId, Guid commandId, CancellationToken cancellationToken = default)
    {
        var clientResult = await _clientService.EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return Result.Failure(clientResult.Error);
        }

        var cancelled = await _queue.CancelAsync(clientResult.Value, commandId, cancellationToken);
        if (cancelled is null)
        {
            return Result.Failure(NotQueued);
        }

        // The command is out of the queue whether or not this is heard.
        try
        {
            await _eventBus.PublishAsync(new ClientCommandCancelledEvent(clientRefId, cancelled.ClientId, workspaceId, cancelled.Type, commandId), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Queued command '{CommandId}' was cancelled, but the cancellation could not be announced. {Message}", commandId, ex.Message);
        }

        return Result.Success();
    }

    // A command that waits is delivered later by background work with no request in scope, so who sent
    // it is named now, from this request, and stored with it.
    private void StampActor(ActorEvent @event)
    {
        if (_identity is not null)
        {
            ActorStampingEventBus.Stamp(@event, _identity, _requestOrigin);
        }
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
            && (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.Add(CommandQueueOptions.MaxLifetime)))
        {
            return Error.Validation("COMMAND_EXPIRY_INVALID", "expiresAt must be in the future and at most 24 hours away.");
        }

        return null;
    }

    private static Error? ValidateOta(ClientOtaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url)
            || request.Url.Length > MaxOtaUrlLength
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(url.Host))
        {
            return Error.Validation("OTA_URL_INVALID", $"url must be an https URL of at most {MaxOtaUrlLength} characters.");
        }

        if (request.Sha256 is not { Length: 64 } || !request.Sha256.All(char.IsAsciiHexDigit))
        {
            return Error.Validation("OTA_SHA256_INVALID", "sha256 must be the image's SHA-256 as 64 hex digits.");
        }

        if (request.Version is { Length: > MaxOtaVersionLength })
        {
            return Error.Validation("OTA_VERSION_INVALID", $"version is limited to {MaxOtaVersionLength} characters.");
        }

        if (request.Size is <= 0 or > uint.MaxValue)
        {
            return Error.Validation("OTA_SIZE_INVALID", "size must be the image's size in bytes, more than 0.");
        }

        if (request.ExpiresAt is { } expiresAt
            && (expiresAt <= DateTimeOffset.UtcNow || expiresAt > DateTimeOffset.UtcNow.Add(CommandQueueOptions.MaxLifetime)))
        {
            return Error.Validation("COMMAND_EXPIRY_INVALID", "expiresAt must be in the future and at most 24 hours away.");
        }

        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
