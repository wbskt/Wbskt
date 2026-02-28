using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public abstract class BaseNode
{
    public Guid NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<PortDefinition> Ports { get; set; } = new();
}

public class PortDefinition
{
    public string PortId { get; set; } = "default";
    public PortDirection Direction { get; set; } = PortDirection.Out;
}

public abstract class BaseTrigger : BaseNode
{
}

public abstract class BaseAction : BaseNode
{
}

public abstract class BaseControl : BaseNode
{
    // Marker for logic, flow, and data processing nodes.
}
