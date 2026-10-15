using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface INodeExecutor
{
    string Kind { get; }

    Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct);

    /// <summary>
    /// Whether this node can run again after a crash without a second effect. The branch row only
    /// moves its pointer after the node has run, so a crash in between replays the node on the next
    /// dispatch. A replay-safe node is simply run again; any other node claims an idempotency key per
    /// attempt, and a replay of the same key returns the recorded outcome instead of executing.
    /// </summary>
    /// <remarks>
    /// Answer per node, not per executor: one executor can be safe for one configuration and not for
    /// another (a local Variable write lands in the branch row and is replayed with it, a shared
    /// Increment writes outside it and would count twice). A node is replay-safe only when running
    /// it again changes nothing outside the branch row and reaches the same outcome; a write that is
    /// repeated with the same value qualifies, a counter or a fresh identifier does not. The default
    /// is the conservative answer: an executor that does not say otherwise is deduplicated, which
    /// costs a row per attempt but never repeats an effect.
    /// </remarks>
    bool IsReplaySafe(BaseNode node) => false;
}
