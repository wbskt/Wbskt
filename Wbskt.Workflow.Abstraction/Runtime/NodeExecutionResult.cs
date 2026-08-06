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

    /// <summary>
    /// Move to the node wired to <paramref name="OutboundPort"/>.
    /// </summary>
    /// <param name="LocalStatePatch">Keys to add or overwrite in the branch's local state.</param>
    /// <param name="RemoveKeys">
    /// Keys to delete from local state. Needed by nodes that can be revisited in a loop and must
    /// clear their own bookkeeping on the way out - a sequential ForEach dropping its iterator, or a
    /// Delay dropping its deadline so the next lap waits again. Without removal, a patch could only
    /// ever add or overwrite, so stale markers would make the second visit behave like a resume.
    /// </param>
    public sealed record Continue(
        string OutboundPort,
        IReadOnlyDictionary<string, JsonElement> LocalStatePatch,
        IReadOnlyCollection<string>? RemoveKeys = null) : NodeExecutionResult;

    public sealed record Fork(IReadOnlyCollection<ForkSpec> Children, string? ContinueOutboundPort, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;

    public sealed record WaitForBookmark(WakeCondition Condition, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult;

    public sealed record Fail(string ErrorCode, string Message, bool Retryable, [property: JsonIgnore] Exception? Cause) : NodeExecutionResult;

    public sealed record Terminal(BranchTerminalReason Reason) : NodeExecutionResult;
}

public sealed record ForkSpec(string OutboundPort, IReadOnlyDictionary<string, JsonElement> LocalState);

public enum BranchTerminalReason { Completed, Cancelled, Failed }
