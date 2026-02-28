using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Nodes.Controls;

public class AggregateNode : BaseControl
{
    public sealed override List<PortDefinition> Ports { get; set; } =
    [
        new PortDefinition { PortId = PortNames.In, Direction = PortDirection.In },
        new PortDefinition { PortId = PortNames.Out, Direction = PortDirection.Out }
    ];

    public string Function { get; set; } = "Sum"; // Sum, Average, Min, Max, Count
    public string DataExpression { get; set; } = string.Empty;
    public int TimeWindowSeconds { get; set; }
}