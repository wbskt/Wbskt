using System.Text.Json;
using System.Text.Json.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed class BranchContext
{
    public required Guid BranchRefId { get; init; }

    public required Guid NodeId { get; init; }

    public required JsonObject Local { get; init; }

    public Dictionary<string, object?> LocalBag { get; init; } = new();

    public Guid? ParentBranchRefId { get; init; }

    public Guid? ForkCohortId { get; init; }

    public JsonElement? LastOutput { get; init; }

    public JsonNode? GetVariable(string key)
    {
        return Local.TryGetPropertyValue(key, out var value) ? value : null;
    }

    public void SetVariable(string key, JsonNode? value)
    {
        if (value is null)
        {
            Local.Remove(key);
        }
        else
        {
            Local[key] = value;
        }
    }

    public void ClearVariable(string key)
    {
        Local.Remove(key);
    }

    public BranchContext DeepClone()
    {
        var clonedLocal = JsonNode.Parse(Local.ToJsonString())?.AsObject() ?? new JsonObject();

        return new BranchContext
        {
            BranchRefId = BranchRefId,
            NodeId = NodeId,
            ParentBranchRefId = ParentBranchRefId,
            ForkCohortId = ForkCohortId,
            Local = clonedLocal,
            LocalBag = new Dictionary<string, object?>(LocalBag),
            LastOutput = LastOutput?.Clone()
        };
    }
}
