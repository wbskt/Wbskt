using System.Collections.Concurrent;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Process-wide map of run id to the token source a branch executing that run observes.
/// </summary>
/// <remarks>
/// Split out of <see cref="RunCancellationService"/> and registered <c>Singleton</c> because the service
/// is <c>Scoped</c>: <c>BranchExecutionPump</c> creates a fresh scope per branch execution, so a
/// per-instance dictionary meant the branch loop minted its token in one scope's registry while every
/// caller of <c>CancelCts</c> - the event consumer, the recovery service, the cancel request itself -
/// cancelled a different, unobserved source in another. Cancellation then only ever took effect at the
/// between-nodes status check, so a cancel issued during a 60-second webhook was not noticed until that
/// webhook finished on its own.
///
/// <para>A <c>static</c> field would have fixed the lifetime just as well and been worse: xUnit runs
/// test classes in one process, so a static registry leaks cancellations between them.</para>
/// </remarks>
internal sealed class RunCancellationTokenRegistry : IDisposable
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _sources = new();

    public CancellationToken GetToken(long runId)
    {
        return _sources.GetOrAdd(runId, _ => new CancellationTokenSource()).Token;
    }

    public void Cancel(long runId)
    {
        // Created already-cancelled when absent, so a cancel that arrives before the branch asks for its
        // token is not lost - the branch then observes a token that is cancelled from the moment it gets it.
        CancellationTokenSource source = _sources.GetOrAdd(runId, _ =>
        {
            var created = new CancellationTokenSource();
            created.Cancel();
            return created;
        });

        if (!source.IsCancellationRequested)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Removed and disposed concurrently by Remove; the run is over either way.
            }
        }
    }

    public void Remove(long runId)
    {
        if (_sources.TryRemove(runId, out CancellationTokenSource? source))
        {
            try
            {
                source.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        foreach (long runId in _sources.Keys)
        {
            Remove(runId);
        }
    }
}
