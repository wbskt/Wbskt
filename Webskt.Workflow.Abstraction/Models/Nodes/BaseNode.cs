namespace Webskt.Workflow.Abstraction.Models.Nodes;

public abstract class BaseNode
{
    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public abstract List<PortDefinition> Ports { get; set; }
}