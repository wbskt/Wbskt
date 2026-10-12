using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Protocol;

namespace Wbskt.Infrastructure.Devices;

/// <summary>What became of a command handed to <see cref="IDeviceCommandSender"/>.</summary>
public enum DeviceCommandOutcome
{
    /// <summary>On the bus, addressed to the socket host holding the device.</summary>
    Sent,

    /// <summary>No such device in the sender's workspace. A device of another workspace reads the same.</summary>
    UnknownClient,

    /// <summary>The type or payload breaks <see cref="DeviceMessageRules"/>; see <see cref="DeviceCommandResult.Violation"/>.</summary>
    Invalid,

    /// <summary>Commands are delivered live or not at all, and nothing holds the device's connection.</summary>
    Offline,

    /// <summary>The broker failed or did not take the command in time; nothing was sent.</summary>
    BrokerUnavailable
}

/// <param name="ClientId">The device's internal id; 0 when <see cref="DeviceCommandOutcome.UnknownClient"/>.</param>
public sealed record DeviceCommandResult(DeviceCommandOutcome Outcome, int ClientId = 0, DeviceMessageViolation? Violation = null);

/// <param name="ExpiresAtUtc">Past this the socket host and the SDK refuse the command instead of acting on it late.</param>
public sealed record DeviceCommand(Guid CommandId, Guid ClientRefId, int WorkspaceId, string Type, string Payload, DateTime? ExpiresAtUtc = null);

/// <summary>
/// Sends a command to a device, the same way for the console and for a workflow step: resolve the
/// device in the sender's workspace, apply the protocol rules, refuse an offline device, and publish
/// addressed to the socket host that holds it. The caller maps the outcome: management to HTTP, the
/// engine to the step's port.
/// </summary>
public interface IDeviceCommandSender
{
    /// <param name="attribute">Sets who sent the command on the event, when the bus would not know.</param>
    Task<DeviceCommandResult> SendAsync(DeviceCommand command, Action<ClientCommandEvent>? attribute, CancellationToken cancellationToken);
}

public sealed class DeviceCommandSender(
    IClientCommandTargetProvider targets,
    IEventBus eventBus,
    ILogger<DeviceCommandSender> logger) : IDeviceCommandSender
{
    /// <summary>How long a command waits for the broker before the sender is told it is unavailable.</summary>
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    public async Task<DeviceCommandResult> SendAsync(DeviceCommand command, Action<ClientCommandEvent>? attribute, CancellationToken cancellationToken)
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
            logger.LogInformation("Client '{ClientRefId}' is offline; command '{Type}' not sent.", command.ClientRefId, command.Type);
            return new DeviceCommandResult(DeviceCommandOutcome.Offline, target.ClientId);
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
}

public static class DeviceCommandServiceCollectionExtensions
{
    /// <summary>The one way a host sends a command to a device. Needs the platform database and the bus.</summary>
    public static IServiceCollection AddDeviceCommandSender(this IServiceCollection services)
    {
        services.AddScoped<IClientCommandTargetProvider, ClientCommandTargetProvider>();
        services.AddScoped<IDeviceCommandSender, DeviceCommandSender>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
