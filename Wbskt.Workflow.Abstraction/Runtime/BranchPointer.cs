using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record BranchPointer(
    string NodeId,
    int Attempt,
    IReadOnlyDictionary<string, JsonElement> LocalState
)
{
    public static BranchPointer From(BranchContext context)
    {
        return new BranchPointer(
            context.CurrentNodeId,
            context.Attempt,
            context.LocalState
        );
    }
}
