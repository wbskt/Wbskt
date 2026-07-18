using System.Text.Json;
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;

namespace Wbskt.Workflow.Abstraction.Runtime;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Continue), "Continue")]
[JsonDerivedType(typeof(Fork), "Fork")]
[JsonDerivedType(typeof(WaitForBookmark), "WaitForBookmark")]
[JsonDerivedType(typeof(Fail), "Fail")]
[JsonDerivedType(typeof(Terminal), "Terminal")]
public abstract record NodeExecutionResult
{
    public static Continue JumpTo(Guid nodeId, IReadOnlyDictionary<string, JsonElement> localStatePatch)
    {
        return new Continue(nodeId.ToString(), localStatePatch);
    }

    public sealed record Continue(string OutboundPort, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;

    public sealed record Fork(IReadOnlyCollection<ForkSpec> Children, string? ContinueOutboundPort, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;

    public sealed record WaitForBookmark(WakeCondition Condition, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;

    public sealed record Fail(string ErrorCode, string Message, bool Retryable, [property: JsonIgnore] Exception? Cause) : NodeExecutionResult;

    public sealed record Terminal(BranchTerminalReason Reason) : NodeExecutionResult;
}

public sealed record ForkSpec(string OutboundPort, IReadOnlyDictionary<string, JsonElement> LocalState);

public enum BranchTerminalReason { Completed, Cancelled, Failed }
