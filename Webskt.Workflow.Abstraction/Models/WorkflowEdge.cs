namespace Webskt.Workflow.Abstraction.Models;

public class WorkflowEdge
{
    public Guid EdgeId { get; set; }
    public WorkflowPort Source { get; set; } = new();
    public WorkflowPort Target { get; set; } = new();

    /// <summary>
    /// Edge-specific properties, such as labels (e.g., "BATTERY") 
    /// or metadata seen in the UI.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}
