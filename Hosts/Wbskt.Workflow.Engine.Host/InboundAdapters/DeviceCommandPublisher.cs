using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Sends a workflow's command the way the console does: addressed to the socket host holding the
/// device, and reported as failed at once when nothing holds it, rather than dropped unseen.
/// </summary>
public sealed class DeviceCommandPublisher(
    IEventBus eventBus,
    IClientCommandTargetProvider targets,
    ILogger<DeviceCommandPublisher> logger) : IDeviceCommandPublisher
{
    internal const string UnknownClientReason = "The device does not exist in this workspace.";

    public async Task PublishCommandAsync(Guid clientRefId, int workspaceId, string command, string payload, CommandSender? sentBy, CancellationToken ct)
    {
        var target = await targets.FindAsync(clientRefId, ct);

        // A device of another workspace is answered like a missing one: a workflow addresses only its own.
        if (target is null || target.WorkspaceId != workspaceId)
        {
            logger.LogWarning("No client '{ClientRefId}' in workspace {WorkspaceId}; command '{Command}' not sent.", clientRefId, workspaceId, command);
            await eventBus.PublishAsync(Attributed(new ClientCommandFailedEvent(clientRefId, 0, workspaceId, command, UnknownClientReason, Guid.NewGuid()), sentBy), ct);
            return;
        }

        // The validator refuses these at publish time; this catches a definition saved before it did.
        if (DeviceMessageRules.FindViolation(command, payload) is { } violation)
        {
            logger.LogWarning("Command '{Command}' to client '{ClientRefId}' refused: {Reason}", command, clientRefId, violation);
            await eventBus.PublishAsync(Attributed(new ClientCommandFailedEvent(clientRefId, target.ClientId, workspaceId, command, violation, Guid.NewGuid()), sentBy), ct);
            return;
        }

        if (!target.IsConnected || string.IsNullOrEmpty(target.HostId))
        {
            logger.LogInformation("Client '{ClientRefId}' is offline; command '{Command}' reported as failed.", clientRefId, command);
            await eventBus.PublishAsync(Attributed(new ClientCommandFailedEvent(clientRefId, target.ClientId, workspaceId, command, ClientCommandFailedEvent.NotConnectedReason, Guid.NewGuid()), sentBy), ct);
            return;
        }

        var @event = new ClientCommandEvent(clientRefId, target.ClientId, workspaceId, command, payload, Guid.NewGuid(), TargetHostId: target.HostId);
        await eventBus.PublishAsync(Attributed(@event, sentBy), ct);
    }

    private static TEvent Attributed<TEvent>(TEvent @event, CommandSender? sentBy) where TEvent : BaseEvent
    {
        if (sentBy is not null && @event is ActorEvent actor)
        {
            actor.ActorSource = EventSource.Workflow;
            actor.ActorWorkflowRefId = sentBy.WorkflowRefId;
            actor.ActorRunRefId = sentBy.RunRefId;
        }

        return @event;
    }
}
