using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models;

public class WaitAllNode : BaseControl
{
    public WaitAllNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }
}

public class WaitAnyNode : BaseControl
{
    public WaitAnyNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }
}

public class LoopNode : BaseControl
{
    public LoopNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "body", Direction = PortDirection.Out });
        Ports.Add(new PortDefinition { PortId = "completed", Direction = PortDirection.Out });
    }

    public string ItemsExpression { get; set; } = string.Empty;
    public string IteratorName { get; set; } = "item";
}

public class DelayNode : BaseControl
{
    public DelayNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public int Seconds { get; set; }
}

public class RateLimitNode : BaseControl
{
    public RateLimitNode()
    {
        Ports.Add(new PortDefinition { PortId = "in", Direction = PortDirection.In });
        Ports.Add(new PortDefinition { PortId = "out", Direction = PortDirection.Out });
    }

    public int Limit { get; set; }
    public int PeriodSeconds { get; set; }
}
