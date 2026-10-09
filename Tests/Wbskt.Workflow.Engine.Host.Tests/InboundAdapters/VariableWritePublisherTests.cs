using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class VariableWritePublisherTests
{
    private static readonly VariableWrite Write = new(Guid.NewGuid(), 11, Guid.NewGuid(), 3, "hits", "Increment", "42");

    [Fact]
    public async Task A_run_write_is_published_with_the_run_as_its_actor()
    {
        var bus = new Mock<IEventBus>();
        SharedVariableSetByRunEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<SharedVariableSetByRunEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SharedVariableSetByRunEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await new VariableWritePublisher(bus.Object, NullLogger<VariableWritePublisher>.Instance).PublishAsync(Write, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal((Write.WorkflowRefId, 11, 3, "hits", "Increment", "42"),
            (captured!.WorkflowRefId, captured.WorkflowId, captured.WorkspaceId, captured.Name, captured.Operation, captured.Value));
        Assert.Equal(EventSource.Workflow, captured.ActorSource);
        Assert.Equal(Write.WorkflowRefId, captured.ActorWorkflowRefId);
        Assert.Equal(Write.RunRefId, captured.ActorRunRefId);
    }

    [Fact]
    public async Task A_long_value_is_cut_and_a_broker_failure_does_not_fail_the_write()
    {
        var bus = new Mock<IEventBus>();
        SharedVariableSetByRunEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<SharedVariableSetByRunEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SharedVariableSetByRunEvent, CancellationToken>((e, _) => captured = e)
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var publish = () => new VariableWritePublisher(bus.Object, NullLogger<VariableWritePublisher>.Instance)
            .PublishAsync(Write with { ValueJson = new string('x', 1000) }, CancellationToken.None);

        await publish();
        Assert.Equal(SharedVariableValues.MaxLength + 1, captured!.Value!.Length);
    }
}
