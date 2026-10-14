using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// What the branch loop does with one kind of <see cref="NodeExecutionResult"/>, as an
/// <see cref="INodeExecutor"/> is what the engine does with one kind of node. There is one handler per
/// result type, and <see cref="BranchResultDispatcher"/> picks it by the result's type.
///
/// Handlers are registered Scoped, like the executors, and hold nothing per branch: the branch, its run,
/// the definition and the node all arrive in <see cref="BranchResultContext"/>, so one instance serves
/// every branch its scope runs.
/// </summary>
internal interface IBranchResultHandler
{
    /// <summary>The <see cref="NodeExecutionResult"/> subtype this handler handles.</summary>
    Type ResultType { get; }

    /// <summary>Handles a result of <see cref="ResultType"/>.</summary>
    Task<BranchResultOutcome> HandleAsync(NodeExecutionResult result, BranchResultContext context, CancellationToken ct);
}

/// <inheritdoc cref="IBranchResultHandler"/>
internal interface IBranchResultHandler<TResult> : IBranchResultHandler
    where TResult : NodeExecutionResult
{
    Task<BranchResultOutcome> HandleAsync(TResult result, BranchResultContext context, CancellationToken ct);
}

/// <summary>
/// The base of every handler: binds <see cref="IBranchResultHandler.ResultType"/> to
/// <typeparamref name="TResult"/>, so a handler cannot claim one type and cast to another.
/// </summary>
internal abstract class BranchResultHandler<TResult> : IBranchResultHandler<TResult>
    where TResult : NodeExecutionResult
{
    public Type ResultType => typeof(TResult);

    public abstract Task<BranchResultOutcome> HandleAsync(TResult result, BranchResultContext context, CancellationToken ct);

    Task<BranchResultOutcome> IBranchResultHandler.HandleAsync(NodeExecutionResult result, BranchResultContext context, CancellationToken ct)
    {
        return HandleAsync((TResult)result, context, ct);
    }
}
