using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// A workflow's command, sent by the same <see cref="IClientCommandSender"/> as the console's. A
/// command for an offline client waits in its queue, as the console's does, and ends as a
/// <see cref="ClientCommandFailedEvent"/> if the client does not come back before the step's ack
/// timeout, which is the command's expiry. A command that cannot be sent or queued at all is reported
/// at once as a <see cref="ClientCommandFailedEvent"/>, which takes a waiting step out by its "failed"
/// port, rather than dropped unseen. A broker that is down is thrown, for the step to fail and the run
/// to retry.
/// </summary>
/// <remarks>
/// The queue also covers a run started by a client's first message before its connection is recorded
/// (#141): the command waits, and the connect delivers it, rather than the step re-reading presence.
/// </remarks>
public sealed class ClientCommandPublisher(IClientCommandSender sender, IEventBus eventBus, TimeProvider clock) : IClientCommandPublisher
{
    internal const string UnknownClientReason = "The client does not exist in this workspace.";
    internal const string QueueFullReason = "The client is offline and already has as many commands waiting as it may.";
    internal const string ExpiryInvalidReason = "The step's ack timeout is longer than a command may wait.";
    internal const string InactiveReason = "The client is offline and not registered, so the command cannot wait for it.";

    public async Task PublishCommandAsync(Guid commandId, Guid clientRefId, int workspaceId, string command, string payload, TimeSpan expiresIn, CommandSender? sentBy, CancellationToken ct)
    {
        var request = new ClientCommand(commandId, clientRefId, workspaceId, command, payload, clock.GetUtcNow().UtcDateTime + expiresIn);
        var result = await sender.SendAsync(request, e => Attribute(e, sentBy), ct);

        string? reason = result.Outcome switch
        {
            ClientCommandOutcome.Sent or ClientCommandOutcome.Queued => null,
            ClientCommandOutcome.UnknownClient => UnknownClientReason,
            ClientCommandOutcome.QueueFull => QueueFullReason,
            ClientCommandOutcome.Inactive => InactiveReason,
            // The validator refuses these at publish time; this catches a definition saved before it did.
            ClientCommandOutcome.Invalid => result.Violation!.Message,
            ClientCommandOutcome.ExpiryInvalid => ExpiryInvalidReason,
            ClientCommandOutcome.BrokerUnavailable => throw new InvalidOperationException("The event bus is unavailable; the command was not sent."),
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
