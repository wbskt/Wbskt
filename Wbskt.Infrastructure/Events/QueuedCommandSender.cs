using Wbskt.EventBus.Abstractions;

namespace Wbskt.Infrastructure.Events;

/// <summary>
/// An <see cref="ICommandSender"/> that never waits for the broker, for the same case as
/// <see cref="QueuedEventBus"/>: a command that follows a change already committed (the runs of a
/// deleted workflow), which a broker outage should delay rather than fail. The command shares the
/// event queue, so it goes out in order with the events and is retried the same way.
/// </summary>
public sealed class QueuedCommandSender(QueuedEventBus queue, ICommandSender sender) : ICommandSender
{
    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : class, ICommand
    {
        queue.Queue(typeof(TCommand).Name, token => sender.SendAsync(command, token));
        return Task.CompletedTask;
    }
}
