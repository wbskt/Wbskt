using System.Collections.Frozen;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>Hands a node's result to the handler registered for its type.</summary>
internal interface IBranchResultDispatcher
{
    /// <exception cref="InvalidOperationException">No handler is registered for the result's type.</exception>
    Task<BranchResultOutcome> DispatchAsync(NodeExecutionResult result, BranchResultContext context, CancellationToken ct);
}

/// <summary>
/// The result-type counterpart of <see cref="NodeExecutorRegistry"/>: every registered
/// <see cref="IBranchResultHandler"/>, keyed by the result type it handles. A result type with no
/// handler throws, naming the type, rather than being skipped.
/// </summary>
internal sealed class BranchResultDispatcher : IBranchResultDispatcher
{
    private readonly FrozenDictionary<Type, IBranchResultHandler> _handlers;

    public BranchResultDispatcher(IEnumerable<IBranchResultHandler> handlers)
    {
        var dictionary = new Dictionary<Type, IBranchResultHandler>();
        foreach (var handler in handlers)
        {
            if (!dictionary.TryAdd(handler.ResultType, handler))
            {
                throw new InvalidOperationException($"Duplicate branch result handler for {handler.ResultType.Name}");
            }
        }

        _handlers = dictionary.ToFrozenDictionary();
    }

    public Task<BranchResultOutcome> DispatchAsync(NodeExecutionResult result, BranchResultContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        // By exact type: every result is a sealed record, so there is no subtype to fall back to.
        if (_handlers.TryGetValue(result.GetType(), out IBranchResultHandler? handler))
        {
            return handler.HandleAsync(result, context, ct);
        }

        throw new InvalidOperationException($"A node returned {result.GetType().Name}, which the branch loop does not handle.");
    }
}
