using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record NodeContext
{
    public required BranchContext Branch { get; init; }

    public required BaseNode Node { get; init; }

    /// <summary>
    /// The workflow being executed, for the few executors that must reason about the graph rather
    /// than just their own node - e.g. ParallelForEach resolving the Join its cohort converges on.
    ///
    /// Optional rather than required: the engine always supplies it, but the many executor unit
    /// tests that build a context by hand only need it when the executor under test walks the graph.
    /// </summary>
    public WorkflowDefinition? Definition { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Where an executor notes what it wants on the run's history without failing the step. The retry
    /// executor hands them on with the step's result, so only the attempt that decides the step counts.
    /// </summary>
    public List<NodeWarning> Warnings { get; } = [];
}
