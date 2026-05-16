namespace Wbskt.Workflow.Engine.Host.V2;

/// <summary>
/// Thread-safe, shared key/value state for a single workflow runtime instance.
/// All branches of the same runtime read and write to the same RuntimeState.
/// </summary>
public sealed class RuntimeState
{
    private readonly Dictionary<string, object?> _globals;

    public RuntimeState(Dictionary<string, object?> initial)
    {
        _globals = new Dictionary<string, object?>(initial, StringComparer.OrdinalIgnoreCase);
    }

    public object? Get(string key)
    {
        lock (_globals)
            return _globals.GetValueOrDefault(key);
    }

    public void Set(string key, object? value)
    {
        lock (_globals)
            _globals[key] = value;
    }
}

