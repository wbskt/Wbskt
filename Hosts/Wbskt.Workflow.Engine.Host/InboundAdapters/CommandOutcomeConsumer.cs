using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Turns what became of a command into the command channel, where a send-command step waiting for
/// it is woken. Most commands have no step waiting (the console's, or a step that does not wait);
/// for those the hub finds nothing and records nothing.
/// </summary>
public sealed class CommandOutcomeConsumer(IInboundHub hub) : IConsumer<ClientCommandAckedEvent>, IConsumer<ClientCommandFailedEvent>
{
    public Task Consume(ConsumeContext<ClientCommandAckedEvent> context) =>
        hub.HandleAsync(CommandChannel.Event(context.Message.CommandId, CommandChannel.Acked, null), context.CancellationToken);

    public Task Consume(ConsumeContext<ClientCommandFailedEvent> context)
    {
        // A failure reported before commands carried ids has nothing to wake.
        return context.Message.CommandId is Guid commandId
            ? hub.HandleAsync(CommandChannel.Event(commandId, CommandChannel.Failed, context.Message.Reason), context.CancellationToken)
            : Task.CompletedTask;
    }
}
