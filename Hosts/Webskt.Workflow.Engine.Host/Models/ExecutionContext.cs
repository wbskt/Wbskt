using Webskt.Workflow.Engine.Host.Models.TriggerContexts;

namespace Webskt.Workflow.Engine.Host.Models;

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
    /// The typed context that started this workflow.
    /// </summary>
    public BaseTriggerContext? TriggerContext => _instance.TriggerContext;

    /// <summary>
    /// The direct output from the PREVIOUS node in this specific branch.
    /// Useful for chaining nodes without creating explicit variables.
    /// </summary>
    public object? LastNodeOutput { get; set; }

    public object? GetState(string key) => _instance.State.GetValueOrDefault(key);
    
    public void SetState(string key, object? value)
    {
        lock (_instance.State)
        {
            _instance.State[key] = value;
        }
    }
}
