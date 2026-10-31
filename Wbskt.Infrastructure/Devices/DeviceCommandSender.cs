using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Protocol;

namespace Wbskt.Infrastructure.Devices;

/// <summary>What became of a command handed to <see cref="IDeviceCommandSender"/>.</summary>
public enum DeviceCommandOutcome
{
    /// <summary>On the bus, addressed to the socket host holding the device.</summary>
    Sent,

    /// <summary>
    /// The device is offline, so the command waits in its queue until it connects or the command
    /// expires (<see cref="DeviceCommandResult.ExpiresAtUtc"/>).
    /// </summary>
    Queued,

    /// <summary>The device is offline and already has as many commands waiting as it may.</summary>
    QueueFull,

    /// <summary>The device is offline and not registered (pending, revoked or rejected), so nothing can wait for it.</summary>
    Inactive,

    /// <summary>No such device in the sender's workspace. A device of another workspace reads the same.</summary>
    UnknownClient,

    /// <summary>The type or payload breaks <see cref="DeviceMessageRules"/>; see <see cref="DeviceCommandResult.Violation"/>.</summary>
    Invalid,

    /// <summary>The broker failed or did not take the command in time; nothing was sent.</summary>
    BrokerUnavailable
}

/// <param name="ClientId">The device's internal id; 0 when <see cref="DeviceCommandOutcome.UnknownClient"/>.</param>
/// <param name="ExpiresAtUtc">When a <see cref="DeviceCommandOutcome.Queued"/> command stops waiting.</param>
public sealed record DeviceCommandResult(DeviceCommandOutcome Outcome, int ClientId = 0, DeviceMessageViolation? Violation = null, DateTime? ExpiresAtUtc = null);

/// <param name="ExpiresAtUtc">Past this the socket host and the SDK refuse the command instead of acting on it late.</param>
public sealed record DeviceCommand(Guid CommandId, Guid ClientRefId, int WorkspaceId, string Type, string Payload, DateTime? ExpiresAtUtc = null);

/// <summary>
/// Sends a command to a device, the same way for the console and for a workflow step: resolve the
/// device in the sender's workspace, apply the protocol rules, and publish addressed to the socket
/// host that holds it, or queue it for an offline device to take when it connects. The caller maps
/// the outcome: management to HTTP, the engine to the step's port.
/// </summary>
public interface IDeviceCommandSender
{
    /// <param name="attribute">Sets who sent the command on its events, when the bus would not know.</param>
    Task<DeviceCommandResult> SendAsync(DeviceCommand command, Action<ActorEvent>? attribute, CancellationToken cancellationToken);
}

public sealed class DeviceCommandSender(
    IClientCommandTargetProvider targets,
    DeviceCommandQueue queue,
    IEventBus eventBus,
    ILogger<DeviceCommandSender> logger) : IDeviceCommandSender
{
    /// <summary>How long a command waits for the broker before the sender is told it is unavailable.</summary>
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task<DeviceCommandResult> SendAsync(DeviceCommand command, Action<ActorEvent>? attribute, CancellationToken cancellationToken)
    {
        // Ownership has to be settled here: the command goes out over the bus and the socket host
        // routes it by ClientRefId alone, so nothing downstream would notice a client from another
        // workspace.
        var target = await targets.FindAsync(command.ClientRefId, cancellationToken);
        if (target is null || target.WorkspaceId != command.WorkspaceId)
        {
            logger.LogInformation("No client '{ClientRefId}' in workspace {WorkspaceId}; command '{Type}' not sent.", command.ClientRefId, command.WorkspaceId, command.Type);
            return new DeviceCommandResult(DeviceCommandOutcome.UnknownClient);
        }

        if (DeviceMessageRules.Check(command.Type, command.Payload) is { } violation)
        {
            logger.LogInformation("Command '{Type}' to client '{ClientRefId}' refused: {Reason}", command.Type, command.ClientRefId, violation.Message);
            return new DeviceCommandResult(DeviceCommandOutcome.Invalid, target.ClientId, violation);
        }

        if (!target.IsConnected || string.IsNullOrEmpty(target.HostId))
        {
            return await QueueAsync(command, target, attribute, cancellationToken);
        }

        var @event = new ClientCommandEvent(command.ClientRefId, target.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId,
            TargetHostId: target.HostId, ExpiresAtUtc: command.ExpiresAtUtc);
        attribute?.Invoke(@event);

        // The publish is the action: a command reported as sent must have reached the broker, so a
        // broker that fails or does not answer in time is reported, rather than a hung caller.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PublishTimeout);
        try
        {
            await eventBus.PublishAsync(@event, timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Publishing command '{CommandId}' failed; the event bus is unavailable. {Message}", command.CommandId, ex.Message);
            return new DeviceCommandResult(DeviceCommandOutcome.BrokerUnavailable, target.ClientId);
        }

        return new DeviceCommandResult(DeviceCommandOutcome.Sent, target.ClientId);
    }

    private async Task<DeviceCommandResult> QueueAsync(DeviceCommand command, ClientCommandTarget target, Action<ActorEvent>? attribute, CancellationToken cancellationToken)
    {
        // A device that is not registered will not sign in to take it.
        if (target.Status != ClientStatus.Registered)
        {
            logger.LogInformation("Client '{ClientRefId}' is offline and {Status}; command '{Type}' not queued.", command.ClientRefId, target.Status, command.Type);
            return new DeviceCommandResult(DeviceCommandOutcome.Inactive, target.ClientId);
        }

        var expiresAt = queue.ExpiryFor(command.ExpiresAtUtc);
        var announcement = new ClientCommandQueuedEvent(command.ClientRefId, target.ClientId, command.WorkspaceId, command.Type, command.Payload, command.CommandId, expiresAt);
        attribute?.Invoke(announcement);

        if (!await queue.QueueAsync(QueuedCommand.From(command, target.ClientId, expiresAt, announcement), announcement, queue.Options.MaxPerClient, cancellationToken))
        {
            logger.LogInformation("Client '{ClientRefId}' is offline with a full queue; command '{Type}' not queued.", command.ClientRefId, command.Type);
            return new DeviceCommandResult(DeviceCommandOutcome.QueueFull, target.ClientId);
        }

        logger.LogInformation("Client '{ClientRefId}' is offline; command '{CommandId}' ('{Type}') queued until {ExpiresAt:O}.", command.ClientRefId, command.CommandId, command.Type, expiresAt);
        return new DeviceCommandResult(DeviceCommandOutcome.Queued, target.ClientId, ExpiresAtUtc: expiresAt);
    }
}

public static class DeviceCommandServiceCollectionExtensions
{
    /// <summary>
    /// The one way a host sends a command to a device, and the queue commands for an offline device
    /// wait in. Needs the platform database and the bus.
    /// </summary>
    public static IServiceCollection AddDeviceCommandSender(this IServiceCollection services)
    {
        services.AddScoped<IClientCommandTargetProvider, ClientCommandTargetProvider>();
        services.AddScoped<IQueuedCommandStore, QueuedCommandStore>();
        services.AddScoped<DeviceCommandQueue>();
        services.AddScoped<IDeviceCommandSender, DeviceCommandSender>();
        services.AddOptions<CommandQueueOptions>().BindConfiguration(CommandQueueOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
