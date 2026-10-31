using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// A workflow's command, sent by the same <see cref="IDeviceCommandSender"/> as the console's. A
/// command for an offline device waits in its queue, as the console's does, and ends as a
/// <see cref="ClientCommandFailedEvent"/> if the device does not come back before the step's ack
/// timeout, which is the command's expiry. A command that cannot be sent or queued at all is reported
/// at once as a <see cref="ClientCommandFailedEvent"/>, which takes a waiting step out by its "failed"
/// port, rather than dropped unseen. A broker that is down is thrown, for the step to fail and the run
/// to retry.
/// </summary>
/// <remarks>
/// The queue also covers a run started by a device's first message before its connection is recorded
/// (#141): the command waits, and the connect delivers it, rather than the step re-reading presence.
/// </remarks>
public sealed class DeviceCommandPublisher(IDeviceCommandSender sender, IEventBus eventBus, TimeProvider clock) : IDeviceCommandPublisher
{
    internal const string UnknownClientReason = "The device does not exist in this workspace.";
    internal const string QueueFullReason = "The device is offline and already has as many commands waiting as it may.";
    internal const string InactiveReason = "The device is offline and not registered, so the command cannot wait for it.";

    public async Task PublishCommandAsync(Guid commandId, Guid clientRefId, int workspaceId, string command, string payload, TimeSpan expiresIn, CommandSender? sentBy, CancellationToken ct)
    {
        var request = new DeviceCommand(commandId, clientRefId, workspaceId, command, payload, clock.GetUtcNow().UtcDateTime + expiresIn);
        var result = await sender.SendAsync(request, e => Attribute(e, sentBy), ct);

        string? reason = result.Outcome switch
        {
            DeviceCommandOutcome.Sent or DeviceCommandOutcome.Queued => null,
            DeviceCommandOutcome.UnknownClient => UnknownClientReason,
            DeviceCommandOutcome.QueueFull => QueueFullReason,
            DeviceCommandOutcome.Inactive => InactiveReason,
            // The validator refuses these at publish time; this catches a definition saved before it did.
            DeviceCommandOutcome.Invalid => result.Violation!.Message,
            DeviceCommandOutcome.BrokerUnavailable => throw new InvalidOperationException("The event bus is unavailable; the command was not sent."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null)
        };

        if (reason is not null)
        {
            await eventBus.PublishAsync(new ClientCommandFailedEvent(clientRefId, result.ClientId, workspaceId, command, reason, commandId), ct);
        }
    }

    private static void Attribute(ActorEvent @event, CommandSender? sentBy)
    {
        if (sentBy is not null)
        {
            @event.ActorSource = EventSource.Workflow;
            @event.ActorWorkflowRefId = sentBy.WorkflowRefId;
            @event.ActorRunRefId = sentBy.RunRefId;
        }
    }
}
