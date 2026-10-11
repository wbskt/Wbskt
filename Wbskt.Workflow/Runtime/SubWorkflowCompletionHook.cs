using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Runtime;

internal sealed class SubWorkflowCompletionHook(IInboundHub hub, IIdGenerator idGenerator) : ISubWorkflowCompletionHook
{
    public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct)
    {
        return hub.HandleAsync(ChildCompletedChannel.Event(runRefId, terminalStatus, idGenerator.NewId()), ct);
    }
}
