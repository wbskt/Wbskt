using System.Text.Json;
using System.Text.Json.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Triggers;

/// <summary>
/// The manual trigger: the passthrough, except that an input the trigger declares and the caller left
/// out of the body takes its default. Applies to Run now and to a parent's sub-workflow step alike.
/// </summary>
internal sealed class ManualTriggerExecutor(TimeProvider clock) : INodeExecutor
{
    private const string BodyKey = "body";

    public string Kind => NodeKind.TriggerManual;

    public bool IsReplaySafe(BaseNode node) => true;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;

        IReadOnlyDictionary<string, JsonElement> patch = new Dictionary<string, JsonElement>
        {
            ["trigger"] = JsonSerializer.SerializeToElement(WithDefaults(ctx.Branch.TriggerPayload, ctx.Node as ManualTriggerNode)),
            ["triggeredAt"] = JsonSerializer.SerializeToElement(clock.GetUtcNow().UtcDateTime)
        };

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue("default", patch));
    }

    internal static IReadOnlyDictionary<string, JsonElement> WithDefaults(IReadOnlyDictionary<string, JsonElement> payload, ManualTriggerNode? node)
    {
        var defaults = node?.Config?.Inputs?.Where(i => i.Default is not null).ToList();
        if (defaults is not { Count: > 0 })
        {
            return payload;
        }

        // A body that is not an object (absent, null, or a bare value) has no keys to keep; defaults are all there is.
        var body = payload.TryGetValue(BodyKey, out var existing) && existing.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(existing.GetRawText())!.AsObject()
            : new JsonObject();

        var changed = false;
        foreach (var input in defaults)
        {
            if (!body.ContainsKey(input.Name))
            {
                body[input.Name] = JsonNode.Parse(input.Default!.Value.GetRawText());
                changed = true;
            }
        }

        if (!changed)
        {
            return payload;
        }

        var filled = new Dictionary<string, JsonElement>(payload) { [BodyKey] = JsonSerializer.SerializeToElement(body) };
        return filled;
    }
}
