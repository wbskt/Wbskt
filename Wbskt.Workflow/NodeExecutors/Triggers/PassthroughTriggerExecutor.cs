using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Triggers;

/// <summary>
/// A trigger executor that records the inbound payload and hands control to the "default" port
/// without any channel-specific logic. Every trigger that simply starts a run this way (client,
/// schedule, webhook, manual) has identical behaviour, so one executor serves them all - the DI
/// container registers one instance per <see cref="NodeKind"/> it covers. A trigger that needs its
/// own behaviour gets its own <see cref="INodeExecutor"/> instead.
/// </summary>
internal sealed class PassthroughTriggerExecutor : INodeExecutor
{
    private readonly IClock _clock;

    public PassthroughTriggerExecutor(string kind, IClock clock)
    {
        Kind = kind;
        _clock = clock;
    }

    public string Kind { get; }

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
