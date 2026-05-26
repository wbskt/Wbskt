using System.Text.Json;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class SubWorkflowCompletionHook(IInboundHub hub, IIdGenerator idGenerator) : ISubWorkflowCompletionHook
{
    public Task OnRunCompletedAsync(Guid runRefId, string terminalStatus, CancellationToken ct)
    {
        InboundEvent evt = new(
            "child-completed",
            $"child-completed:{runRefId}",
            $"child-completed:{runRefId}:{idGenerator.NewId()}",
            new Dictionary<string, JsonElement>
            {
                ["runRefId"] = JsonSerializer.SerializeToElement(runRefId),
                ["status"] = JsonSerializer.SerializeToElement(terminalStatus)
            },
            default);

        return hub.HandleAsync(evt, ct);
    }
}
