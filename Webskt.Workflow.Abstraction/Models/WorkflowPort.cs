namespace Webskt.Workflow.Abstraction.Models;

public class WorkflowPort
{
    public Guid NodeId { get; set; }
    public string PortId { get; set; } = "default";
}