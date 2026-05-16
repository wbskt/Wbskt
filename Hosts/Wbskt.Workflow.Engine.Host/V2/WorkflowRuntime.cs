using System.Threading.Channels;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// A self-contained runtime for one workflow definition.
/// <para>
/// Internally it runs a single <see cref="Channel{T}"/> of <see cref="BranchPointer"/> items.
/// A background loop reads from the channel and fans each pointer into its own independent
/// <c>Task</c> (<see cref="ExecuteBranchAsync"/>).  Fan-out (e.g. from a LogicGate that
/// activates multiple output ports) is handled by forking the <see cref="BranchContext"/>
/// and pushing extra pointers directly onto the channel.
/// </para>
/// </summary>
public sealed class WorkflowRuntime : IWorkflowRuntime
{
    // Pre-compiled lookup structures built once from the WorkflowDefinition.
    private readonly Dictionary<Guid, BaseNode> _nodeIndex;
    private readonly ILookup<Guid, WorkflowEdge> _edgesBySource;

    // Executors are injected; each one declares which node type it handles.
    private readonly IReadOnlyList<INodeExecutorV2> _executors;

    // Global mutable state shared across all branches of this runtime.
    private readonly RuntimeState _state;

    // The branch queue and the single background loop that drains it.
    private readonly Channel<BranchPointer> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingLoop;

    public event Action? OnDisposed;

    public WorkflowRuntime(WorkflowDefinition definition, IEnumerable<INodeExecutorV2> executors)
    {
        _nodeIndex     = definition.Nodes.ToDictionary(n => n.NodeId);
        _edgesBySource = definition.Edges.ToLookup(e => e.Source.NodeId);
        _executors     = executors.ToList();
        _state         = new RuntimeState(definition.InitialState);

        // SingleReader = true because only the loop below consumes from the channel.
        _channel = Channel.CreateUnbounded<BranchPointer>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false, // forked branches also write to the channel
            AllowSynchronousContinuations = false,
        });

        _processingLoop = Task.Run(ProcessLoopAsync);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="InboundManager"/> when an external event arrives that
    /// matches the trigger node identified by <paramref name="nodeRef"/>.
    /// Creates a fresh <see cref="BranchContext"/> and enqueues the first pointer.
    /// </summary>
    public Task ExecuteTriggerAsync(Guid nodeRef, object trigger)
    {
        var branch = new BranchContext(trigger, _state);
        _channel.Writer.TryWrite(new BranchPointer { NodeId = nodeRef, Branch = branch });
        return Task.CompletedTask;
    }

    // ── Background loop ───────────────────────────────────────────────────────

    /// <summary>
    /// Drains the channel. Each pointer is handed off to its own Task so that
    /// slow or sleeping branches do not block other branches from starting.
    /// </summary>
    private async Task ProcessLoopAsync()
    {
        try
        {
            await foreach (var pointer in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                // Fire-and-forget: the branch manages its own lifetime.
                _ = ExecuteBranchAsync(pointer, _cts.Token);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown path */ }
    }

    // ── Branch execution ──────────────────────────────────────────────────────

    /// <summary>
    /// Walks one branch synchronously in a tight loop (no channel round-trips per step).
    /// Fan-out edges beyond the first are pushed back onto the channel as forked branches.
    /// </summary>
    private async Task ExecuteBranchAsync(BranchPointer seed, CancellationToken ct)
    {
        var nodeId = seed.NodeId;
        var branch = seed.Branch;

        while (!ct.IsCancellationRequested)
        {
            // 1. Resolve the node definition.
            if (!_nodeIndex.TryGetValue(nodeId, out var node))
                break; // dangling edge – end of branch

            // 2. Find an executor that can handle this node type.
            var executor = _executors.FirstOrDefault(e => e.CanExecute(node));
            if (executor is null)
                break; // no executor registered – treat as terminal

            // 3. Execute the node.
            NodeExecutionResultV2 result;
            try
            {
                result = await executor.ExecuteAsync(node, branch, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // TODO: publish a fault event / structured log
                _ = ex;
                break;
            }

            // 4. Handle failure.
            if (!result.IsSuccess)
                break;

            // 5. Handle inline delay (short waits like DelayNode).
            //    The branch keeps its goroutine; use a persisted timer for long waits.
            if (result.Delay.HasValue)
            {
                await Task.Delay(result.Delay.Value, ct);
                // After a delay the branch ends here – the node must re-enqueue
                // itself if it wants to continue (or the caller handles resume).
                break;
            }

            // 6. Carry the node's output forward so the next node can read $output.
            branch.LastOutput = result.Output;

            // 7. Find the edges that leave this node via the activated ports.
            var nextEdges = _edgesBySource[node.NodeId]
                .Where(e => result.ActivatedPortIds.Contains(e.Source.PortId))
                .ToList();

            if (nextEdges.Count == 0)
                break; // terminal node – branch complete

            // 8. Fan-out: the first edge continues inline on this goroutine.
            //    Every additional edge is a forked branch pushed onto the channel.
            for (int i = 1; i < nextEdges.Count; i++)
            {
                _channel.Writer.TryWrite(new BranchPointer
                {
                    NodeId = nextEdges[i].Target.NodeId,
                    Branch = branch.Fork() // independent copy of local vars, shared globals
                });
            }

            // 9. Advance the pointer to the next node (inline, no channel overhead).
            nodeId = nextEdges[0].Target.NodeId;
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Signal no more work will arrive, then cancel in-flight branches.
        _channel.Writer.Complete();
        await _cts.CancelAsync();

        try   { await _processingLoop; }
        catch (OperationCanceledException) { }
        finally { _cts.Dispose(); }

        OnDisposed?.Invoke();
    }
}