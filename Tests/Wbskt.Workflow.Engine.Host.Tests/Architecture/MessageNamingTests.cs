using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;

namespace Wbskt.Workflow.Engine.Host.Tests.Architecture;

/// <summary>
/// An event says what happened and is published to whoever listens, including the audit log; a
/// command asks one service to do something and is sent to its queue. The names say which is which,
/// so a reader never has to open the type to know whether it lands in the audit log.
/// </summary>
public sealed class MessageNamingTests
{
    private static readonly Type[] Messages = typeof(CancelWorkflowRun).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray();

    [Fact]
    public void Every_event_is_named_for_what_happened()
    {
        var unnamed = Messages.Where(t => typeof(IEvent).IsAssignableFrom(t) && !t.Name.EndsWith("Event", StringComparison.Ordinal)).Select(t => t.Name);

        Assert.Empty(unnamed);
    }

    [Fact]
    public void A_command_is_not_an_event_and_is_not_named_like_one()
    {
        var commands = Messages.Where(t => typeof(ICommand).IsAssignableFrom(t)).ToList();

        Assert.Contains(typeof(CancelWorkflowRun), commands);
        Assert.Empty(commands.Where(t => typeof(IEvent).IsAssignableFrom(t) || t.Name.EndsWith("Event", StringComparison.Ordinal)).Select(t => t.Name));
    }
}
