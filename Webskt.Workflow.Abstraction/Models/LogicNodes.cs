using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class LogicGateNode : BaseControl
{
    public LogicGateNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "Match", Direction = PortDirection.Out });
        Ports.Add(new PortDefinition { PortId = "Otherwise", Direction = PortDirection.Out });
    }

    public required WorkflowExpression Condition { get; set; }
}

public class SwitchNode : BaseControl
{
    public SwitchNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "Default", Direction = PortDirection.Out });
    }

    public string Expression { get; set; } = string.Empty;
}
