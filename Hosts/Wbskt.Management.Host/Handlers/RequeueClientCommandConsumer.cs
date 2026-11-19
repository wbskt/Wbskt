using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Protocol;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Clients;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Puts back in the queue a command whose client dropped before its socket host could deliver it, so
/// it waits for the next connect like any command sent to an offline client. A command whose expiry
/// has already passed goes in too, and the expiry sweep reports it, so there is one place that does.
/// A command for a client that lost its registration meanwhile is ended as failed instead: queued, it
/// would be delivered if the client were approved again before it expired. So is one for a client
/// deleted meanwhile, so a workflow step waiting on its acknowledgement leaves as failed rather than
/// waiting out its timeout.
/// </summary>
public sealed class RequeueClientCommandConsumer : IConsumer<RequeueClientCommand>
{
    private readonly ClientCommandQueue _queue;
    private readonly IClientCommandTargetProvider _targets;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RequeueClientCommandConsumer> _logger;

    public RequeueClientCommandConsumer(ClientCommandQueue queue, IClientCommandTargetProvider targets, IEventBus eventBus, ILogger<RequeueClientCommandConsumer> logger)
    {
        _queue = queue;
        _targets = targets;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RequeueClientCommand> context)
    {
        var message = context.Message;

        // A revoke whose event is handled after the row below is queued drops it through
        // ClientCommandQueueRevocationHandler. One handled in the moment between this read and the
        // insert is missed, and the command waits until it expires: approving the client again in
        // that time would deliver it. Narrow, and no worse than before this check.
        var target = await _targets.FindAsync(message.ClientRefId, context.CancellationToken);
        if (target is null)
        {
            await EndAsDeletedAsync(message, context.CancellationToken);
            return;
        }

        if (target.Status != ClientStatus.Registered)
        {
            _logger.LogInformation("Command '{CommandId}' was handed back for client '{ClientRefId}', which is no longer registered: ended as failed.", message.CommandId, message.ClientRefId);
            await _eventBus.PublishAsync(new ClientCommandFailedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, message.Type,
                ClientCommandFailedEvent.RevokedWhileQueuedReason, message.CommandId), context.CancellationToken);
            return;
        }

        var expiresAt = _queue.ExpiryFor(message.ExpiresAtUtc);
        var announcement = new ClientCommandQueuedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, message.Type, message.Payload, message.CommandId, expiresAt)
        {
            ActorUserId = message.ActorUserId,
            ActorUserRefId = message.ActorUserRefId,
            ActorSource = message.ActorSource,
            ActorWorkflowRefId = message.ActorWorkflowRefId,
            ActorRunRefId = message.ActorRunRefId
        };
        var command = QueuedCommand.From(
            new ClientCommand(message.CommandId, message.ClientRefId, message.WorkspaceId, message.Type, message.Payload),
            message.ClientId,
            expiresAt,
            announcement);

        // Past the per-client limit: the command was accepted once already.
        var handBack = message.ReturnedByHostId is { } hostId ? new CommandHandBack(hostId, message.ReturnedAtUtc) : null;
        if (!await _queue.QueueAsync(command, announcement, int.MaxValue, handBack, context.CancellationToken))
        {
            await EndAsDeletedAsync(message, context.CancellationToken);
        }
    }

    private async Task EndAsDeletedAsync(RequeueClientCommand message, CancellationToken ct)
    {
        _logger.LogInformation("Command '{CommandId}' was handed back for client '{ClientRefId}', which no longer exists: ended as failed.", message.CommandId, message.ClientRefId);
        await _eventBus.PublishAsync(new ClientCommandFailedEvent(message.ClientRefId, message.ClientId, message.WorkspaceId, message.Type,
            ClientCommandFailedEvent.DeletedWhileQueuedReason, message.CommandId), ct);
    }
}
