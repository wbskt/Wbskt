using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Triggers;

internal sealed class ScheduleTriggerExecutor : INodeExecutor
{
    private readonly IClock _clock;

    public ScheduleTriggerExecutor(IClock clock)
    {
        _clock = clock;
    }

    public string Kind => NodeKind.TriggerSchedule;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;

        IReadOnlyDictionary<string, JsonElement> patch = new Dictionary<string, JsonElement>
        {
            ["trigger"] = JsonSerializer.SerializeToElement(ctx.Branch.TriggerPayload),
            ["triggeredAt"] = JsonSerializer.SerializeToElement(_clock.UtcNow)
        };

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue("default", patch));
    }
}
