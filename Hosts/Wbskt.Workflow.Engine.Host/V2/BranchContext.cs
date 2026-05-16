namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// Carries the execution state for a single branch of a workflow run.
/// <para>
/// Global state (workflow-level variables) is shared via <see cref="RuntimeState"/>
/// and is visible to all concurrent branches. Local variables are branch-private
/// and are used for things like loop iterators.
/// </para>
/// </summary>
public sealed class BranchContext
{
    private readonly RuntimeState _globalState;

    public BranchContext(object triggerPayload, RuntimeState globalState)
    {
        TriggerPayload = triggerPayload;
        _globalState = globalState;
    }

    // Private constructor used by Fork() to clone local variables.
    private BranchContext(object triggerPayload, RuntimeState globalState, Dictionary<string, object?> localVars)
    {
        TriggerPayload = triggerPayload;
        _globalState = globalState;
        LocalVariables = new Dictionary<string, object?>(localVars, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The raw payload that triggered this branch (e.g., a device telemetry event).</summary>
    public object TriggerPayload { get; }

    /// <summary>The output produced by the previous node in this branch.</summary>
    public object? LastOutput { get; set; }

    /// <summary>Branch-private variables (e.g., loop iterator, temporary calculations).</summary>
    public Dictionary<string, object?> LocalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    // ── Global state accessors ────────────────────────────────────────────────

    public object? GetGlobal(string key) => _globalState.Get(key);
    public void SetGlobal(string key, object? value) => _globalState.Set(key, value);

    // ── Local variable accessors ──────────────────────────────────────────────

    public object? GetLocal(string key) => LocalVariables.GetValueOrDefault(key);
    public void SetLocal(string key, object? value) => LocalVariables[key] = value;

    // ── Combined lookup (local wins over global) ──────────────────────────────

    public object? GetVariable(string key) =>
        LocalVariables.TryGetValue(key, out var local) ? local : _globalState.Get(key);

    // ── Forking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a child branch that shares the same <see cref="RuntimeState"/> but
    /// gets its own copy of the local variables and the current <see cref="LastOutput"/>.
    /// </summary>
    public BranchContext Fork() => new(TriggerPayload, _globalState, LocalVariables)
    {
        LastOutput = LastOutput
    };
}

