namespace Wbskt.EventBus.Abstractions;

/// <summary>
/// A request for one service to do something, sent to that service's queue and handled once. Named in
/// the imperative (<c>CancelWorkflowRun</c>). Not an <see cref="IEvent"/>: an event is a past-tense
/// record of something that happened (<c>WorkflowRunCancelRequestedEvent</c>), published to whoever
/// listens and kept in the audit log; a command is neither.
/// </summary>
public interface ICommand
{
    /// <summary>The durable queue the handling service reads this command from.</summary>
    static abstract string QueueName { get; }
}

public interface ICommandSender
{
    /// <summary>Sends <paramref name="command"/> to its queue and returns once the broker has it.</summary>
    Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : class, ICommand;
}

public static class CommandQueue
{
    public static Uri Address<TCommand>() where TCommand : ICommand => new($"queue:{TCommand.QueueName}");
}
