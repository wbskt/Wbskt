using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Infrastructure.Devices;

namespace Wbskt.Management.Host.Handlers;

/// <summary>
/// Puts back in the queue a command whose device dropped before its socket host could deliver it, so
/// it waits for the next connect like any command sent to an offline device. A command whose expiry
/// has already passed goes in too, and the expiry sweep reports it, so there is one place that does.
/// </summary>
public sealed class RequeueClientCommandConsumer : IConsumer<RequeueClientCommand>
{
    private readonly DeviceCommandQueue _queue;
    private readonly ILogger<RequeueClientCommandConsumer> _logger;

    public RequeueClientCommandConsumer(DeviceCommandQueue queue, ILogger<RequeueClientCommandConsumer> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RequeueClientCommand> context)
    {
        var message = context.Message;
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
            new DeviceCommand(message.CommandId, message.ClientRefId, message.WorkspaceId, message.Type, message.Payload),
            message.ClientId,
            expiresAt,
            announcement);

        // Past the per-device limit: the command was accepted once already.
        if (!await _queue.QueueAsync(command, announcement, int.MaxValue, context.CancellationToken))
        {
            _logger.LogInformation("Command '{CommandId}' was not put back in the queue: client '{ClientRefId}' no longer exists.", message.CommandId, message.ClientRefId);
        }
    }
}
