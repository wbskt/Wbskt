namespace Webskt.Workflow.Abstraction.Models;

public sealed class ExecutionContext
{
    private readonly WorkflowInstance _instance;

    public ExecutionContext(WorkflowInstance instance, Guid pointerId)
    {
        _instance = instance;
        PointerId = pointerId;
    }

    public Guid PointerId { get; }
    public Guid InstanceId => _instance.InstanceId;

    /// <summary>
    /// Read-only access to what triggered the workflow.
    /// </summary>
    public object? TriggerData => _instance.TriggerData;

    /// <summary>
    /// Read/Write access to the shared instance state.
    /// </summary>
    public object? GetState(string key) => _instance.State.GetValueOrDefault(key);
    
    public void SetState(string key, object? value)
    {
        lock (_instance.State) // Ensure thread-safety for parallel branches
        {
            _instance.State[key] = value;
        }
    }
}
