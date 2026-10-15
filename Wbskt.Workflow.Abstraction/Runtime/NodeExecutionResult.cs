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

    public sealed record WaitForBookmark(WakeCondition Condition, IReadOnlyDictionary<string, JsonElement> LocalStatePatch) : NodeExecutionResult
    {
        /// <summary>
        /// Keys to delete from local state as the branch parks, before the patch is applied. A parking
        /// node drops a stale wake this way, so the wake that ends this park is the only one on the branch.
        /// </summary>
        public IReadOnlyCollection<string>? RemoveKeys { get; init; }

        /// <summary>
        /// Runs once the branch is parked and its bookmark exists: for a node whose wake answers
        /// something it sends, sending it any earlier would let a fast answer arrive with nothing
        /// waiting for it. Not part of a cached result, so a node replayed from its idempotency key
        /// does not send again; its wait then ends by its TTL.
        /// </summary>
        [JsonIgnore]
        public Func<CancellationToken, Task>? AfterParked { get; init; }
    }

    public sealed record Fail(string ErrorCode, string Message, bool Retryable, [property: JsonIgnore] Exception? Cause) : NodeExecutionResult;

    public sealed record Terminal(BranchTerminalReason Reason) : NodeExecutionResult
    {
        /// <summary>
        /// The run's output, from a <c>control:end</c> node that declares one: recorded on the run before
        /// the branch completes, so the finalizer hands it to a waiting parent. Null for any other end.
        /// </summary>
        public JsonElement? Output { get; init; }
    }
}

public sealed record ForkSpec(string OutboundPort, IReadOnlyDictionary<string, JsonElement> LocalState);

public enum BranchTerminalReason { Completed, Cancelled, Failed }
